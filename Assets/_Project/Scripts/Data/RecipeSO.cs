using System;
using System.Collections.Generic;
using UnityEngine;

namespace Celestia.Data
{
    /// <summary>
    /// One crafting recipe: a multiset of <see cref="Ingredient"/>s in → one
    /// <see cref="outputItem"/> × <see cref="outputCount"/> out.
    ///
    /// The crafting UI doesn't care which input slot holds which ingredient — only the
    /// total counts per item type matter. <see cref="Matches"/> takes the 4 input boxes
    /// (a list of nullable items) and returns true if their per-type histogram equals
    /// the recipe's required histogram.
    /// </summary>
    [CreateAssetMenu(menuName = "Celestia/Recipe", fileName = "Recipe_New")]
    public sealed class RecipeSO : ScriptableObject
    {
        [Serializable]
        public struct Ingredient
        {
            public ItemDefinitionSO item;
            [Min(1)] public int count;
        }

        [Header("Identity")]
        public string displayName;

        [TextArea(2, 4)]
        public string description;

        [Header("Recipe")]
        [Tooltip("Items the player must place across the input slots. Order doesn't matter " +
                 "— only the per-type counts. The total ingredient count must equal the number " +
                 "of filled input boxes.")]
        public Ingredient[] inputs = Array.Empty<Ingredient>();

        public ItemDefinitionSO outputItem;
        [Min(1)] public int outputCount = 1;

        public bool Matches(IList<ItemDefinitionSO> craftingInputs)
        {
            // Histogram of what's currently in the boxes (skip nulls = empty boxes).
            var hist = new Dictionary<ItemDefinitionSO, int>();
            int totalInBoxes = 0;
            for (int i = 0; i < craftingInputs.Count; i++)
            {
                var item = craftingInputs[i];
                if (item == null) continue;
                hist.TryGetValue(item, out int c);
                hist[item] = c + 1;
                totalInBoxes++;
            }

            // Must use exactly as many ingredient instances as the recipe requires —
            // stops a single wood matching a recipe that wants four.
            int totalRequired = 0;
            for (int i = 0; i < inputs.Length; i++) totalRequired += inputs[i].count;
            if (totalInBoxes != totalRequired) return false;

            // And each ingredient's count must match exactly.
            for (int i = 0; i < inputs.Length; i++)
            {
                var ing = inputs[i];
                if (ing.item == null) continue;
                hist.TryGetValue(ing.item, out int c);
                if (c != ing.count) return false;
            }
            return true;
        }
    }
}
