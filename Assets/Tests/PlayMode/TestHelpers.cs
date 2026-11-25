using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

public static class TestHelpers
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Component FindComponent(string typeName)
    {
        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
        {
            if (mb != null && mb.GetType().Name == typeName) return mb;
        }

        return null;
    }

    public static List<Component> FindComponents(string typeName)
    {
        var list = new List<Component>();
        foreach (MonoBehaviour mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
        {
            if (mb != null && mb.GetType().Name == typeName) list.Add(mb);
        }

        return list;
    }

    public static object Get(object target, string member)
    {
        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(member, Flags);
        if (property != null) return property.GetValue(target);
        FieldInfo field = type.GetField(member, Flags);
        if (field != null) return field.GetValue(target);
        throw new MissingMemberException(type.Name, member);
    }

    public static T Get<T>(object target, string member) => (T)Get(target, member);

    public static object Call(object target, string method, params object[] args)
    {
        MethodInfo info = target.GetType().GetMethod(method, Flags, null, Array.ConvertAll(args, a => a.GetType()), null);
        Assert.IsNotNull(info, $"{target.GetType().Name}.{method} não encontrado.");
        return info.Invoke(target, args);
    }

    public static Component Comp(GameObject go, string typeName) => go.GetComponent(typeName);

    public static IEnumerator WaitRealtime(float seconds)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < end) yield return null;
    }

    public static IEnumerator WaitUntilRealtime(Func<bool> condition, float timeout, string failMessage)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        Assert.IsTrue(condition(), failMessage);
    }

    public static IEnumerator WaitForDungeon()
    {
        yield return WaitUntilRealtime(() => GameObject.Find("Dungeon_Generated") != null, 20f, "A dungeon não foi gerada.");
        yield return WaitRealtime(1f);
    }

    public static void Teleport(GameObject character, Vector3 position)
    {
        Rigidbody body = character.GetComponent<Rigidbody>();
        if (body != null) body.position = position;
        character.transform.position = position;
        Physics.SyncTransforms();
    }

    public static GameObject Character(string name)
    {
        GameObject go = GameObject.Find(name);
        Assert.IsNotNull(go, $"Personagem {name} não encontrado.");
        return go;
    }

    public static void ResetPause()
    {
        Type type = Type.GetType("GamePause, Assembly-CSharp");
        type?.GetMethod("ForceRelease", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
        Time.timeScale = 1f;
    }
}

public abstract class MatchFixture : InputTestFixture
{
    public override void Setup()
    {
        DisableLeftoverPlayerInput();
        TestHelpers.ResetPause();
        base.Setup();
    }

    public override void TearDown()
    {
        TestHelpers.ResetPause();
        DisableLeftoverPlayerInput();
        base.TearDown();
    }

    private static void DisableLeftoverPlayerInput()
    {
        foreach (PlayerInput player in UnityEngine.Object.FindObjectsByType<PlayerInput>(FindObjectsInactive.Exclude)) player.gameObject.SetActive(false);
        foreach (PlayerInputManager manager in UnityEngine.Object.FindObjectsByType<PlayerInputManager>(FindObjectsInactive.Exclude)) manager.gameObject.SetActive(false);
    }

    protected IEnumerator StartMatch(List<Gamepad> pads, int count)
    {
        for (int i = 0; i < count; i++) pads.Add(InputSystem.AddDevice<Gamepad>());

        SceneManager.LoadScene("Game");
        yield return null;
        yield return null;

        foreach (Gamepad pad in pads) yield return Tap(pad.buttonSouth);
        yield return TestHelpers.WaitRealtime(0.5f);
        foreach (Gamepad pad in pads) yield return Tap(pad.buttonSouth);
        yield return TestHelpers.WaitForDungeon();
    }

    protected IEnumerator Tap(ButtonControl button)
    {
        Press(button);
        yield return null;
        yield return null;
        Release(button);
        yield return null;
        yield return null;
    }

    protected IEnumerator Push(StickControl stick, Vector2 direction)
    {
        Set(stick, direction);
        yield return null;
        yield return null;
        Set(stick, Vector2.zero);
        yield return null;
        yield return null;
    }
}
