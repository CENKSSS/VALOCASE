using UnityEngine;
using ValoCase.Data;

namespace ValoCase.UI
{
    public sealed class UltraRevealEffect : MonoBehaviour
    {
        [SerializeField] ParticleSystem burst;
        [SerializeField] Animator animator;

        public void Play(SkinDefinitionSO skin)
        {
            if (skin == null || skin.Rarity != SkinRarity.Ultra) return;
            if (burst != null) burst.Play();
            if (animator != null) animator.SetTrigger("UltraReveal");
        }
    }
}
