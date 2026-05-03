using System;
using UnityEngine;

public class SoldierHealth : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 30f;

    public event Action<SoldierHealth> OnDied;

    private float currentHealth;
    private bool isDead;

    private SoldierAI ai;
    private Animator animator;
    private Rigidbody rb;

    void Awake()
    {
        currentHealth = maxHealth;
        ai = GetComponent<SoldierAI>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody>();

        Debug.Log($"[{name}] SoldierHealth initialized. HP = {currentHealth}");
    }

    public void TakeDamage(float damage)
    {
        if (isDead) return;

        currentHealth -= damage;
        Debug.Log($"[{name}] Took damage: {damage}. HP now: {currentHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public float destroyDelay = 2f;

    void Die()
    {
        if (isDead) return;
        isDead = true;

        Debug.Log($"[{name}] Soldier died.");

        if (ai != null)
            ai.enabled = false;

        if (animator != null)
            animator.enabled = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        OnDied?.Invoke(this);

        Destroy(gameObject, destroyDelay);
    }

    public bool IsDead()
    {
        return isDead;
    }
}