using UnityEngine;

public static class BallisticModel
{
    public struct Params
    {
        // geometry / mass
        public float massKg;
        public float radiusM;
        public float area; // cross section

        // environment
        public Vector3 gravity;   // m/s^2
        public float airDensity;  // kg/m^3
        public Vector3 wind;      // m/s

        // drag
        public float dragCd;      // sphere ~0.47

        // magnus / hop-up
        public float magnusLiftSlope;   // Cl ≈ slope * (ωr/v)
        public float maxLiftCoefficient;

        // spin
        public float spinDecayRate; // 1/s
    }

    public struct State
    {
        public Vector3 pos;    // m
        public Vector3 vel;    // m/s
        public Vector3 omega;  // rad/s (world)
    }

    public static float CrossSectionArea(float radiusM) => Mathf.PI * radiusM * radiusM;

    // Энергетика: v0 = sqrt(2E/m)
    public static float MuzzleSpeedFromEnergy(float energyJ, float massKg)
        => Mathf.Sqrt(Mathf.Max(0f, 2f * energyJ / Mathf.Max(massKg, 1e-6f)));

    // axis для backspin: хотим, чтобы (omega_hat x v_hat) давало примерно "вверх"
    // если v ~ forward, то omega ~ right.
    public static Vector3 BackspinAxisForVelocity(Vector3 vDir)
    {
        Vector3 axis = Vector3.Cross(Vector3.up, vDir);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
        return axis.normalized;
    }

    /// Один шаг интегрирования точечной массы:
    /// sumF = Fg + Fd + Fm,   a = sumF/m
    /// Semi-implicit Euler:
    /// v += a*dt;  x += v*dt
    /// </summary>
    public static void Step(ref State s, in Params p, float dt, Vector3 spinWobbleAccelRadPerSec2)
    {
        // 1) spin update (decay + wobble)
        if (p.spinDecayRate > 0f)
            s.omega *= Mathf.Exp(-p.spinDecayRate * dt);

        s.omega += spinWobbleAccelRadPerSec2 * dt;

        // 2) forces
        Vector3 vRel = s.vel - p.wind;

        Vector3 Fg = p.massKg * p.gravity;
        Vector3 Fd = DragForceQuadratic(vRel, p.airDensity, p.dragCd, p.area);
        Vector3 Fm = MagnusForce(vRel, s.omega, p.airDensity, p.area, p.radiusM, p.magnusLiftSlope, p.maxLiftCoefficient);

        Vector3 a = (Fg + Fd + Fm) / Mathf.Max(p.massKg, 1e-6f);

        // 3) integrate
        s.vel += a * dt;
        s.pos += s.vel * dt;
    }

    // Fd = -0.5*rho*Cd*A*|v|*v
    static Vector3 DragForceQuadratic(Vector3 vRel, float rho, float cd, float area)
    {
        float speed = vRel.magnitude;
        if (speed < 1e-5f) return Vector3.zero;
        return -0.5f * rho * cd * area * speed * vRel;
    }

    // Magnus:
    // S = (|ω| r)/|v|,  Cl ≈ slope*S (clamped)
    // Fm = 0.5*rho*A*Cl*v^2 * liftDir, liftDir = normalize( ω̂ × v̂ )
    static Vector3 MagnusForce(
        Vector3 vRel,
        Vector3 omega,
        float rho,
        float area,
        float radiusM,
        float liftSlope,
        float maxCl)
    {
        float speed = vRel.magnitude;
        float omegaMag = omega.magnitude;
        if (speed < 1e-5f || omegaMag < 1e-5f) return Vector3.zero;

        Vector3 vDir = vRel / speed;
        Vector3 omegaDir = omega / omegaMag;

        float S = (omegaMag * radiusM) / speed;
        float Cl = Mathf.Clamp(liftSlope * S, -maxCl, maxCl);

        Vector3 liftDir = Vector3.Cross(omegaDir, vDir);
        float liftLen = liftDir.magnitude;
        if (liftLen < 1e-6f) return Vector3.zero;
        liftDir /= liftLen;

        return 0.5f * rho * area * Cl * speed * speed * liftDir;
    }
}