using UnityEngine;

public static class PunchEffectPlayer
{
    public static GameObject Play(
        GameObject prefab,
        Transform spawnPoint,
        Transform fallbackPoint,
        Vector3 localOffset,
        Vector3 rotationEuler,
        float scaleMultiplier,
        float playbackSpeed,
        float destroyDelay,
        bool parentToSpawnPoint,
        bool activateChildren,
        string debugOwner = "")
    {
        if (prefab == null) return null;

        Transform anchor = spawnPoint != null ? spawnPoint : fallbackPoint;
        Vector3 spawnPosition = anchor != null ? anchor.TransformPoint(localOffset) : localOffset;
        Quaternion spawnRotation = Quaternion.Euler(rotationEuler);
        Transform parent = parentToSpawnPoint ? anchor : null;

        GameObject effect = Object.Instantiate(prefab, spawnPosition, spawnRotation, parent);
        effect.name = $"{prefab.name} (Punch Effect)";

        float safeScale = scaleMultiplier > 0f ? scaleMultiplier : 1f;
        float safePlaybackSpeed = playbackSpeed > 0f ? playbackSpeed : 1f;
        effect.transform.localScale = prefab.transform.localScale * safeScale;

        SetActiveRecursively(effect.transform, true);

        ParticleSystem[] particles = effect.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem particle in particles)
        {
            if (particle == null) continue;

            ParticleSystem.MainModule main = particle.main;
            main.simulationSpeed = safePlaybackSpeed;

            if (!particle.gameObject.activeSelf)
            {
                particle.gameObject.SetActive(true);
            }

            particle.Clear(true);
            particle.Play(true);
        }

        Animator[] animators = effect.GetComponentsInChildren<Animator>(true);
        foreach (Animator effectAnimator in animators)
        {
            if (effectAnimator == null) continue;

            if (!effectAnimator.gameObject.activeSelf)
            {
                effectAnimator.gameObject.SetActive(true);
            }

            effectAnimator.enabled = true;
            effectAnimator.speed = safePlaybackSpeed;
            effectAnimator.Rebind();
            effectAnimator.Update(0f);
        }

        effect.BroadcastMessage("Play", SendMessageOptions.DontRequireReceiver);

        float safeDestroyDelay = Mathf.Max(0.01f, destroyDelay);
        Object.Destroy(effect, safeDestroyDelay);

        if (!string.IsNullOrEmpty(debugOwner))
        {
            Debug.Log($"[{debugOwner}] เล่น punch effect: {effect.name} | point={(anchor != null ? anchor.name : "world")} | ParticleSystems={particles.Length}, Animators={animators.Length}");
        }

        return effect;
    }

    private static void SetActiveRecursively(Transform target, bool isActive)
    {
        target.gameObject.SetActive(isActive);

        foreach (Transform child in target)
        {
            SetActiveRecursively(child, isActive);
        }
    }
}
