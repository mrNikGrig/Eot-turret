using UnityEngine;
using System;
using System.Collections.Generic;

public class Bullet : MonoBehaviour
{
    [Header("Damage / Life")]
    public float damage = 10f;
    public float lifeTime = 5f;

    [Header("BB (Point-mass)")]
    public float bbMassGrams = 0.25f;
    public float bbDiameterMm = 5.95f;

    [Tooltip("Muzzle energy in Joules. v0 = sqrt(2E/m).")]
    public float muzzleEnergyJ = 1.2f;

    [Header("Environment")]
    public Vector3 gravity = new Vector3(0f, -9.81f, 0f);
    public float airDensity = 1.225f;
    public Vector3 wind = Vector3.zero;

    [Header("Quadratic Drag")]
    public float dragCd = 0.47f;

    [Header("Hop-up / Magnus")]
    public float hopUpSpinRps = 150f;
    public float magnusLiftSlope = 1.2f;
    public float maxLiftCoefficient = 1.0f;

    [Header("Spin decay & instability")]
    public float spinDecayRate = 1.5f;
    public float spinWobbleRadPerSec2 = 0.0f;

    [Header("Shot dispersion (per shot)")]
    public float angularStdDeg = 0.0f;          
    public float speedStdFraction = 0.0f;       
    public float spinStdFraction = 0.0f;
    public float spinAxisStdDeg = 0.0f;

    [Header("Collision query")]
    public LayerMask hitMask = ~0;
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Visuals")]
    public bool useTrail = true;
    public float trailDuration = 3f;
    public Color trailColor = Color.yellow;

    private Rigidbody rb;
    private Collider myCol;
    private TrailRenderer tr;

    private BallisticModel.Params p;
    private BallisticModel.State s;
    private bool initialized;
    private bool dying;

    private System.Random rng;
    private HashSet<Collider> ignore = new HashSet<Collider>();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        myCol = GetComponent<Collider>();

        // Мы сами интегрируем движение => PhysX не должен интегрировать.
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
    }

    void Start()
    {
        if (useTrail) SetupTrail();

        // если забыли вызвать InitFromTurret — просто уничтожим по таймеру
        Invoke(nameof(BeginDie), lifeTime);
    }

    public void SetIgnoreColliders(Collider[] cols)
    {
        ignore.Clear();
        if (cols == null) return;
        foreach (var c in cols) if (c != null) ignore.Add(c);
    }

    /// <summary>
    /// Инициализация выстрела: задаёт начальные условия (v0 из энергии, omega из hop-up)
    /// </summary>
    public void InitFromTurret(Vector3 muzzleDirWorld, int seed = -1)
    {
        initialized = true;
        dying = false;

        if (seed < 0) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        rng = new System.Random(seed);

        float massKg = Mathf.Max(1e-6f, bbMassGrams / 1000f);
        float radiusM = Mathf.Max(1e-6f, (bbDiameterMm / 1000f) * 0.5f);

        p = new BallisticModel.Params
        {
            massKg = massKg,
            radiusM = radiusM,
            area = BallisticModel.CrossSectionArea(radiusM),

            gravity = gravity,
            airDensity = airDensity,
            wind = wind,

            dragCd = dragCd,

            magnusLiftSlope = magnusLiftSlope,
            maxLiftCoefficient = maxLiftCoefficient,

            spinDecayRate = spinDecayRate
        };

        // v0 from energy + speed spread
        float v0 = BallisticModel.MuzzleSpeedFromEnergy(muzzleEnergyJ, massKg);
        v0 *= (1f + speedStdFraction * NextGaussian());

        // direction + angular spread
        Vector3 dir = ApplyAngularSpread(muzzleDirWorld.normalized, angularStdDeg);

        // spin axis ~= backspin axis, + axis jitter
        Vector3 spinAxis = BallisticModel.BackspinAxisForVelocity(dir);
        spinAxis = ApplyAxisJitter(spinAxis, spinAxisStdDeg);

        float spinRps = hopUpSpinRps * (1f + spinStdFraction * NextGaussian());
        Vector3 omega = spinAxis * (spinRps * 2f * Mathf.PI);

        s = new BallisticModel.State
        {
            pos = (rb != null) ? rb.position : transform.position,
            vel = dir * v0,
            omega = omega
        };

        // выключаем коллайдер физики (чтобы не было “двойных” столкновений),
        // столкновения ловим SphereCast'ом сами.
        if (myCol != null) myCol.enabled = false;
    }

    void FixedUpdate()
    {
        if (!initialized || dying) return;

        float dt = Time.fixedDeltaTime;

        Vector3 prevPos = s.pos;

        // deterministic wobble for reproducibility (если нужно):
        Vector3 wobbleAccel = (spinWobbleRadPerSec2 > 0f)
            ? RandInsideUnitSphere() * spinWobbleRadPerSec2
            : Vector3.zero;

        BallisticModel.Step(ref s, p, dt, wobbleAccel);

        // move
        if (rb != null) rb.MovePosition(s.pos);
        else transform.position = s.pos;

        // collision by sweep
        CheckHit(prevPos, s.pos);
    }

    void CheckHit(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 1e-6f) return;

        Vector3 dir = delta / dist;

        // SphereCastAll чтобы отфильтровать “игнорируемые” коллайдеры
        RaycastHit[] hits = Physics.SphereCastAll(from, p.radiusM, dir, dist, hitMask, triggerInteraction);
        if (hits == null || hits.Length == 0) return;

        // выбираем ближайший валидный
        float best = float.PositiveInfinity;
        RaycastHit bestHit = default;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null) continue;
            if (ignore.Contains(c)) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                bestHit = hits[i];
                found = true;
            }
        }

        if (!found) return;

        SoldierHealth health = bestHit.collider.GetComponentInParent<SoldierHealth>();
        if (health != null) health.TakeDamage(damage);

        // позиционируем пулю в точке удара (чтобы трейс красиво заканчивался)
        s.pos = bestHit.point;
        if (rb != null) rb.MovePosition(s.pos);
        else transform.position = s.pos;

        BeginDie();
    }

    void BeginDie()
    {
        if (dying) return;
        dying = true;

        if (tr != null) tr.emitting = false;

        // оставляем объект жить ровно столько, сколько нужно хвосту
        float delay = (tr != null) ? tr.time : 0f;
        Destroy(gameObject, delay);
    }

    // -------- visuals --------

    void SetupTrail()
    {
        tr = GetComponent<TrailRenderer>();
        if (tr == null) tr = gameObject.AddComponent<TrailRenderer>();

        tr.time = trailDuration;
        tr.minVertexDistance = 0.02f;
        tr.startWidth = 0.12f;
        tr.endWidth = 0.02f;

        Shader sh =
            Shader.Find("Legacy Shaders/Particles/Additive") ??
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Sprites/Default");

        tr.material = new Material(sh);

        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(trailColor, 0f), new GradientColorKey(trailColor, 1f) },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        tr.colorGradient = g;

        tr.alignment = LineAlignment.View;
    }

    // -------- randomness helpers (deterministic per bullet) --------

    float NextGaussian()
    {
        // Box–Muller
        double u1 = Math.Max(1e-12, rng.NextDouble());
        double u2 = Math.Max(1e-12, rng.NextDouble());
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    Vector3 RandInsideUnitSphere()
    {
        // simple rejection
        while (true)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float y = (float)(rng.NextDouble() * 2.0 - 1.0);
            float z = (float)(rng.NextDouble() * 2.0 - 1.0);
            Vector3 v = new Vector3(x, y, z);
            if (v.sqrMagnitude <= 1f) return v;
        }
    }

    Vector3 ApplyAngularSpread(Vector3 dir, float stdDeg)
    {
        if (stdDeg <= 0f) return dir;

        float yaw = stdDeg * NextGaussian();
        float pitch = stdDeg * NextGaussian();

        Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
        return (q * dir).normalized;
    }

    Vector3 ApplyAxisJitter(Vector3 axis, float stdDeg)
    {
        if (stdDeg <= 0f) return axis;

        Vector3 ortho = Vector3.Cross(axis, Vector3.up);
        if (ortho.sqrMagnitude < 1e-8f) ortho = Vector3.Cross(axis, Vector3.right);
        ortho.Normalize();

        float angle = stdDeg * NextGaussian();
        return (Quaternion.AngleAxis(angle, ortho) * axis).normalized;
    }
}