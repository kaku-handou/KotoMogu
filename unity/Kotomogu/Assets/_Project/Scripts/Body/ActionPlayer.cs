using UnityEngine;

public class ActionPlayer : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private float fadeSeconds = 0.25f;

    public void Play(string actionName)
    {
    int hash = Animator.StringToHash(actionName);

    if (!animator.HasState(0, hash))
    {
        Debug.LogWarning($"[ActionPlayer] 動き '{actionName}' が見つかりません");
        return;
    }

    animator.CrossFadeInFixedTime(hash, fadeSeconds);
    }
}