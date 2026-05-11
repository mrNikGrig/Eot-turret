using UnityEngine;

public static class BallisticsCalculator
{
    public static Vector3 CalculateTrajectory(Vector3 startPos, Vector3 targetPos, float bulletSpeed, float gravity, bool useHighArc = false)
    {
        Vector3 direction = targetPos - startPos;
        float horizontalDistance = new Vector3(direction.x, 0, direction.z).magnitude;
        float verticalDistance = direction.y;

        float speedSquared = bulletSpeed * bulletSpeed;
        float gravityAbs = Mathf.Abs(gravity);
        
        float underRoot = speedSquared * speedSquared - gravityAbs * (gravityAbs * horizontalDistance * horizontalDistance + 2 * verticalDistance * speedSquared);

        if (underRoot < 0)
        {
            return Vector3.zero;
        }

        float angle1 = Mathf.Atan((speedSquared + Mathf.Sqrt(underRoot)) / (gravityAbs * horizontalDistance));
        float angle2 = Mathf.Atan((speedSquared - Mathf.Sqrt(underRoot)) / (gravityAbs * horizontalDistance));

        float angle = useHighArc ? angle1 : angle2;

        Vector3 horizontalDirection = new Vector3(direction.x, 0, direction.z).normalized;

        Vector3 finalDirection = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.Cross(horizontalDirection, Vector3.up)) * horizontalDirection;

        return finalDirection;
    }

    public static Vector3 PredictTargetPosition(Vector3 targetPos, Vector3 targetVelocity, Vector3 firePos, float bulletSpeed)
    {
        Vector3 toTarget = targetPos - firePos;
        float distance = toTarget.magnitude;
        float timeToTarget = distance / bulletSpeed;

        return targetPos + targetVelocity * timeToTarget;
    }
}