using UnityEngine;

public class RandomMover : MonoBehaviour
{
    public float speed = 3f;
    public float changeDirectionTime = 2f;

    private Vector3 direction;

    void Start()
    {
        InvokeRepeating(nameof(ChangeDirection), 0f, changeDirectionTime);
    }

    void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime);
    }

    void ChangeDirection()
    {
        direction = new Vector3(
            Random.Range(-1f, 1f),
            0,
            Random.Range(-1f, 1f)
        ).normalized;
    }
}