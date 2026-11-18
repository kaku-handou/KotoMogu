using UnityEngine;
using UnityEngine.InputSystem;

public class ActionKeyTester : MonoBehaviour
{
    [SerializeField] private ActionPlayer player;

    private void Update()
    {
    if (Keyboard.current.iKey.wasPressedThisFrame) player.Play("idle");
    if (Keyboard.current.fKey.wasPressedThisFrame) player.Play("fly");
    if (Keyboard.current.gKey.wasPressedThisFrame) player.Play("greet");
    if (Keyboard.current.jKey.wasPressedThisFrame) player.Play("jump");
    if (Keyboard.current.sKey.wasPressedThisFrame) player.Play("sit");
    if (Keyboard.current.cKey.wasPressedThisFrame) player.Play("crawl");

    }
}