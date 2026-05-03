using System.Collections;
using UnityEngine;

public class SoldierSpawner : MonoBehaviour
{
    [Header("Spawn")]
    public GameObject soldierPrefab;
    public float respawnDelay = 2f;
    public int maxAliveSoldiers = 5;

    private Collider spawnZone;
    private int aliveSoldiers = 0;

    void Awake()
    {
        spawnZone = GetComponent<Collider>();

        if (spawnZone == null)
            Debug.LogError("SoldierSpawner: no Collider on spawn zone.");
    }

    void Start()
    {
        for (int i = 0; i < maxAliveSoldiers; i++)
        {
            SpawnOneSoldier();
        }
    }

    void SpawnOneSoldier()
    {
        if (soldierPrefab == null)
        {
            Debug.LogError("SoldierSpawner: soldierPrefab is not assigned.");
            return;
        }

        Vector3 pos = GetRandomPoint();
        GameObject soldier = Instantiate(soldierPrefab, pos, Quaternion.identity);

        SoldierHealth health = soldier.GetComponent<SoldierHealth>();
        if (health != null)
        {
            health.OnDied += HandleSoldierDied;
        }
        else
        {
            Debug.LogError($"[{soldier.name}] SoldierHealth not found on prefab.");
        }

        aliveSoldiers++;
        Debug.Log($"Spawned soldier: {soldier.name}. Alive count: {aliveSoldiers}");
    }

    void HandleSoldierDied(SoldierHealth deadSoldier)
    {
        deadSoldier.OnDied -= HandleSoldierDied;
        aliveSoldiers = Mathf.Max(0, aliveSoldiers - 1);

        Debug.Log($"Soldier died: {deadSoldier.name}. Alive count: {aliveSoldiers}");

        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);

        if (aliveSoldiers < maxAliveSoldiers)
            SpawnOneSoldier();
    }

    Vector3 GetRandomPoint()
    {
        Bounds bounds = spawnZone.bounds;

        float x = Random.Range(bounds.min.x, bounds.max.x);
        float z = Random.Range(bounds.min.z, bounds.max.z);
        float y = bounds.max.y + 1f;

        return new Vector3(x, y, z);
    }
}