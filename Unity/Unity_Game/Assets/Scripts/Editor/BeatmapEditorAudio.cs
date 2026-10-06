using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

internal static class BeatmapEditorAudio
{
    private static readonly Type AudioUtilType = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");

    public static void Play(AudioClip clip, int startSample = 0, bool loop = false)
    {
        if (clip == null || AudioUtilType == null)
        {
            return;
        }

        MethodInfo method = AudioUtilType.GetMethod(
            "PlayPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(AudioClip), typeof(int), typeof(bool) },
            null);

        if (method != null)
        {
            method.Invoke(null, new object[] { clip, Mathf.Max(0, startSample), loop });
            return;
        }

        method = AudioUtilType.GetMethod("PlayClip", BindingFlags.Static | BindingFlags.Public);
        method?.Invoke(null, new object[] { clip, Mathf.Max(0, startSample), loop });
    }

    public static void Stop(AudioClip clip)
    {
        if (clip == null || AudioUtilType == null)
        {
            return;
        }

        MethodInfo method = AudioUtilType.GetMethod(
            "StopPreviewClip",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(AudioClip) },
            null);

        if (method != null)
        {
            method.Invoke(null, new object[] { clip });
            return;
        }

        method = AudioUtilType.GetMethod("StopClip", BindingFlags.Static | BindingFlags.Public);
        method?.Invoke(null, new object[] { clip });
    }

    public static void StopAll()
    {
        if (AudioUtilType == null)
        {
            return;
        }

        MethodInfo method = AudioUtilType.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)
            ?? AudioUtilType.GetMethod("StopAllClips", BindingFlags.Static | BindingFlags.Public);
        method?.Invoke(null, null);
    }
}
