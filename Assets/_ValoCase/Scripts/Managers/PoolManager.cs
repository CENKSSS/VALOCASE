using System.Collections.Generic;
using UnityEngine;
using ValoCase.UI;

namespace ValoCase.Pooling
{
    public sealed class PoolManager : MonoBehaviour
    {
        public static PoolManager Instance { get; private set; }

        [SerializeField] ReelItemView reelItemPrefab;
        [SerializeField] SkinCardView skinCardPrefab;
        [SerializeField] Transform reelPoolRoot;
        [SerializeField] Transform cardPoolRoot;
        [SerializeField] int reelPrewarm = 16;
        [SerializeField] int cardPrewarm = 24;

        ObjectPool<ReelItemView> _reelPool;
        ObjectPool<SkinCardView> _cardPool;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _reelPool = new ObjectPool<ReelItemView>(reelItemPrefab, reelPoolRoot, reelPrewarm);
            _cardPool = new ObjectPool<SkinCardView>(skinCardPrefab, cardPoolRoot, cardPrewarm);
        }

        public ReelItemView GetReelItem() => _reelPool.Get();
        public void ReleaseReelItem(ReelItemView item) => _reelPool.Release(item);

        public SkinCardView GetSkinCard() => _cardPool.Get();
        public void ReleaseSkinCard(SkinCardView card) => _cardPool.Release(card);

        public void ReleaseAll(IEnumerable<ReelItemView> reelItems)
        {
            foreach (var item in reelItems) ReleaseReelItem(item);
        }

        public void ReleaseAll(IEnumerable<SkinCardView> cards)
        {
            foreach (var card in cards) ReleaseSkinCard(card);
        }
    }
}
