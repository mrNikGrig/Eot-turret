using UnityEngine;

public class SoldierAI : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2f;
    public float changeDirectionTime = 2.5f;

    [Header("Animator")]
    public string speedParameterName = "Speed";

    private Vector3 direction;
    private Animator animator;
    private bool hasSpeedParameter;

    void Start()
    {
        animator = GetComponent<Animator>();
        hasSpeedParameter = HasFloatParameter(animator, speedParameterName);

        if (animator == null)
        {
            Debug.LogWarning($"[{name}] Animator not found on soldier.");
        }
        else if (!hasSpeedParameter)
        {
            Debug.LogWarning($"[{name}] Animator parameter '{speedParameterName}' not found. Add a float parameter with that exact name.");
        }

        InvokeRepeating(nameof(ChangeDirection), 0f, changeDirectionTime);
    }

    void Update()
    {
        if (!enabled) return;

        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);

        if (direction.sqrMagnitude > 0.0001f)
            transform.forward = direction;

        if (animator != null && hasSpeedParameter)
            animator.SetFloat(speedParameterName, direction.magnitude);
    }

    void ChangeDirection()
    {
        direction = new Vector3(
            Random.Range(-1f, 1f),
            0f,
            Random.Range(-1f, 1f)
        ).normalized;

        Debug.Log($"[{name}] New direction: {direction}");
    }

    bool HasFloatParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;

        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Float && p.name == paramName)
                return true;
        }
        return false;
    }

    public void StopMoving()
    {
        direction = Vector3.zero;
    }
}