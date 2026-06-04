using UnityEngine;

public static class PointMassBallistics
{
    public static float CrossSectionArea(float radiusM) => Mathf.PI * radiusM * radiusM;

    public static float MuzzleSpeedFromEnergy(float energyJ, float massKg)
        => Mathf.Sqrt(Mathf.Max(0f, 2f * energyJ / Mathf.Max(massKg, 1e-6f)));

    public static Vector3 DragForceQuadratic(Vector3 vRel, float airDensity, float dragCd, float area)
    {
        float speed = vRel.magnitude;
        if (speed < 1e-4f) return Vector3.zero;
        return -0.5f * airDensity * dragCd * area * speed * vRel;
    }

    public static Vector3 MagnusForce(
        Vector3 vRel,
        Vector3 omega,
        float airDensity,
        float area,
        float radiusM,
        float magnusLiftSlope,
        float maxLiftCoefficient)
    {
        float speed = vRel.magnitude;
        float omegaMag = omega.magnitude;
        if (speed < 1e-4f || omegaMag < 1e-4f) return Vector3.zero;

        Vector3 vDir = vRel / speed;
        Vector3 omegaDir = omega / omegaMag;

        float S = (omegaMag * radiusM) / speed;
        float Cl = Mathf.Clamp(magnusLiftSlope * S, -maxLiftCoefficient, maxLiftCoefficient);

        Vector3 liftDir = Vector3.Cross(omegaDir, vDir);
        float liftMag = liftDir.magnitude;
        if (liftMag < 1e-5f) return Vector3.zero;
        liftDir /= liftMag;

        return 0.5f * airDensity * area * Cl * speed * speed * liftDir;
    }

    public static Vector3 BackspinAxisForVelocity(Vector3 vDir)
    {
        Vector3 axis = Vector3.Cross(Vector3.up, vDir);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
        return axis.normalized;
    }
}