namespace Atlas2D.Rendering
{
    /// <summary>
    /// Handles rendering every renderable object in the scene. All Render() functions are called from here.
    /// </summary>
    public static class RenderLayerManager
    {
        public static event Action<int, bool> OnLayerRendered;

        private static SortedList<int, List<IRenderable>> _orderedLayerList = new SortedList<int, List<IRenderable>>();
        private static SortedList<int, List<IRenderable>> _orderedLayerListIgnorePostProcessing = new SortedList<int, List<IRenderable>>();

        /// <summary>
        /// Registers or reorders the IRenderable in the render layer sorted list.
        /// </summary>
        /// <param name="renderable">The IRenderable to order in the sorted list.</param>
        /// <param name="reorder">True if the IRenderable already exists in the render layer list.</param>
        /// <param name="oldRenderLayer">Used for reordering. The old render layer of the IRenderable being reordered.</param>
        /// <param name="newRenderLayer">Used for reordering. The new render layer of the IRenderable being reordered.</param>
        public static void OrderRenderable(IRenderable renderable, bool reorder = false, int oldRenderLayer = -1, int newRenderLayer = -1)
        {
            SortedList<int, List<IRenderable>> orderedList = renderable.IgnorePostProcessing ?
                _orderedLayerListIgnorePostProcessing : _orderedLayerList;

            // Removing the old render layer if the IRenderable is reordering itself instead of initializing
            if (reorder)
            {
                // If the render layer is registered in the sorted list, remove the gameobject from the value list
                List<IRenderable> list;
                if (orderedList.TryGetValue(oldRenderLayer, out list))
                {
                    bool removed = list.Remove(renderable);
                    if (!removed) Debug.LogError($"Since item does not exist in layer {oldRenderLayer}, cannot remove it.");
                }
            }

            int renderOrder = reorder ? newRenderLayer : renderable.RenderLayer;
            // If true, the render layer was not in the sorted list yet so it is added
            if (!orderedList.TryAdd(renderOrder, new List<IRenderable> { renderable }))
            {
                // If false, the render layer already exists so the object must be added to an existing value list
                orderedList[renderOrder].Add(renderable);

                // The sorted list class is already sorted in ascending layer, so no extra sorting is necessary
            }
        }

        /// <summary>
        /// Removes the IRenderable from the rendering system (Can still render these manually after removal).
        /// </summary>
        /// <param name="renderable">The IRenderable to remove</param>
        public static void RemoveRenderable(IRenderable renderable)
        {
            SortedList<int, List<IRenderable>> orderedList = renderable.IgnorePostProcessing ?
                _orderedLayerListIgnorePostProcessing : _orderedLayerList;

            // Removing the object from the render order dictionary
            List<IRenderable> list;
            if (orderedList.TryGetValue(renderable.RenderLayer, out list))
            {
                list.Remove(renderable);
            }
        }

        private static void RenderOne(IRenderable renderable)
        {
            try
            {
                renderable.Render();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error rendering {renderable.GetType().Name}: {ex}");
            }
        }

        /// <summary>
        /// Called by the game loop to render all current render layers.
        /// </summary>
        internal static void RenderAllLayers()
        {
            try
            {
                foreach (KeyValuePair<int, List<IRenderable>> pair in _orderedLayerList)
                {
                    OnLayerRendered?.Invoke(pair.Key, false);

                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        RenderOne(pair.Value[i]);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogError($"Tried to create / destroy an IRenderable during the render loop. This is not allowed. {ex}");
            }
        }

        /// <summary>
        /// Called by the game loop to render all current render layers that ignore post processing.
        /// </summary>
        internal static void RenderAllLayersIgnorePostProcessing()
        {
            try
            {
                foreach (KeyValuePair<int, List<IRenderable>> pair in _orderedLayerListIgnorePostProcessing)
                {
                    OnLayerRendered?.Invoke(pair.Key, true);

                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        RenderOne(pair.Value[i]);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogError($"Tried to create / destroy an IRenderable during the render loop. This is not allowed. {ex}");
            }
        }
    }
}
