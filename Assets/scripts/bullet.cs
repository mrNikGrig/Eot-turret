using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
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
    public float airDensity = 1.225f; // kg/m^3
    public Vector3 wind = Vector3.zero; // m/s

    [Header("Quadratic Drag")]
    public float dragCd = 0.47f; // sphere ~0.47

    [Header("Hop-up / Magnus")]
    public float hopUpSpinRps = 150f;      // rev/s
    public float magnusLiftSlope = 1.2f;   // Cl ≈ slope * (ωr/v)
    public float maxLiftCoefficient = 1.0f;

    [Header("Spin decay & instability")]
    public float spinDecayRate = 1.5f;         // 1/s, omega *= exp(-rate*dt)
    public float spinWobbleRadPerSec2 = 5f;    // random angular accel (rad/s^2)

    [Header("Shot dispersion (per shot)")]
    public float angularStdDeg = 0.25f;        // direction spray
    public float speedStdFraction = 0.03f;     // v0 *= (1 + frac*N(0,1))
    public float spinStdFraction = 0.10f;      // spin *= (1 + frac*N(0,1))
    public float spinAxisStdDeg = 2f;          // axis misalignment

    [Header("Visuals")]
    public bool useTrail = true;
    public float trailDuration = 1f;
    public Color trailColor = Color.yellow;

    private Rigidbody rb;

    private bool initialized;
    private float massKg;
    private float radiusM;
    private float area;
    private Vector3 omegaWorld;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = false;

#if UNITY_6000_0_OR_NEWER
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
#else
        rb.drag = 0f;
        rb.angularDrag = 0f;
#endif
    }

    void Start()
    {
        if (useTrail) SetupTrail();
        Destroy(gameObject, lifeTime);

        if (!initialized)
            InitFromTurret(transform.forward);
    }

    public void InitFromTurret(Vector3 muzzleDirWorld)
    {
        initialized = true;

        massKg = Mathf.Max(1e-6f, bbMassGrams / 1000f);
        radiusM = Mathf.Max(1e-6f, (bbDiameterMm / 1000f) * 0.5f);
        area = PointMassBallistics.CrossSectionArea(radiusM);

        float v0 = PointMassBallistics.MuzzleSpeedFromEnergy(muzzleEnergyJ, massKg);
        v0 *= (1f + speedStdFraction * NextGaussian());

        muzzleDirWorld = ApplyAngularSpread(muzzleDirWorld, angularStdDeg);

        Vector3 spinAxis = PointMassBallistics.BackspinAxisForVelocity(muzzleDirWorld);
        spinAxis = ApplyAxisJitter(spinAxis, spinAxisStdDeg);

        float spinRps = hopUpSpinRps * (1f + spinStdFraction * NextGaussian());
        omegaWorld = spinAxis * (spinRps * 2f * Mathf.PI);

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = muzzleDirWorld * v0;
#else
        rb.velocity = muzzleDirWorld * v0;
#endif

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        omegaWorld *= Mathf.Exp(-spinDecayRate * dt);

        omegaWorld += Random.insideUnitSphere * (spinWobbleRadPerSec2 * dt);

#if UNITY_6000_0_OR_NEWER
        Vector3 v = rb.linearVelocity;
#else
        Vector3 v = rb.velocity;
#endif
        Vector3 vRel = v - wind;

        Vector3 Fg = massKg * gravity;
        Vector3 Fd = PointMassBallistics.DragForceQuadratic(vRel, airDensity, dragCd, area);
        Vector3 Fm = PointMassBallistics.MagnusForce(
            vRel, omegaWorld,
            airDensity, area, radiusM,
            magnusLiftSlope, maxLiftCoefficient
        );

        rb.AddForce(Fg + Fd + Fm, ForceMode.Force);

        rb.angularVelocity = omegaWorld;
    }

    void OnCollisionEnter(Collision collision)
    {
        SoldierHealth health = collision.collider.GetComponentInParent<SoldierHealth>();
        if (health != null) health.TakeDamage(damage);
        Destroy(gameObject);
    }

    private TrailRenderer tr;

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
            new[]
            {
                new GradientColorKey(trailColor, 0.0f),
                new GradientColorKey(trailColor, 1.0f),
            },
            new[]
            {
                new GradientAlphaKey(1.0f, 0.0f),
                new GradientAlphaKey(1.0f, 0.7f),
                new GradientAlphaKey(0.0f, 1.0f),
            }
        );
        tr.colorGradient = g;

        tr.alignment = LineAlignment.View;
    }

    static Vector3 ApplyAngularSpread(Vector3 dir, float stdDeg)
    {
        if (stdDeg <= 0f) return dir.normalized;

        float yaw = stdDeg * NextGaussianStatic();
        float pitch = stdDeg * NextGaussianStatic();

        Quaternion q = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right);
        return (q * dir).normalized;
    }

    static Vector3 ApplyAxisJitter(Vector3 axis, float stdDeg)
    {
        if (stdDeg <= 0f) return axis.normalized;

        Vector3 ortho = Vector3.Cross(axis, Vector3.up);
        if (ortho.sqrMagnitude < 1e-8f) ortho = Vector3.Cross(axis, Vector3.right);
        ortho.Normalize();

        float angle = stdDeg * NextGaussianStatic();
        return (Quaternion.AngleAxis(angle, ortho) * axis).normalized;
    }

    float NextGaussian() => NextGaussianStatic();

    static float NextGaussianStatic()
    {
        float u1 = Mathf.Max(1e-6f, Random.value);
        float u2 = Mathf.Max(1e-6f, Random.value);
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }
}