using Atlas2D.Rendering.Shaders;

namespace Atlas2D.Rendering
{
    /// <summary>
    /// An interface that allows classes to register into <see cref="ShaderGlobalUniforms"/> and pass data to all shaders.
    /// </summary>
    public interface IGlobalUniform
    {
        public void ApplyUniform(Shader _shader);
        public bool IsDirty { get; set; }
        public void CheckDirty();
    }

    /// <summary>
    /// Global uniform manager that can apply data to all shaders if they have the appropriate tags.
    /// </summary>
    public static class ShaderGlobalUniforms
    {
        private static Dictionary<string, IGlobalUniform> globalUniforms = new Dictionary<string, IGlobalUniform>();
        private static List<Shader> shaders = new List<Shader>();

        public static void RegisterShader(Shader _shader)
        {
            if (shaders.Contains(_shader)) return;
            shaders.Add(_shader);

            _shader.Use();
            if (_shader.GlobalUniformTags == null) return;
            foreach (string tag in _shader.GlobalUniformTags)
            {
                if (globalUniforms.TryGetValue(tag, out IGlobalUniform uniform))
                {
                    uniform.ApplyUniform(_shader);
                }
            }
        }

        public static void UnregisterShader(Shader _shader)
        {
            shaders.Remove(_shader);
        }

        public static void RegisterGlobalUniform(string _uniformKey, IGlobalUniform _uniformReference)
        {
            globalUniforms.Add(_uniformKey, _uniformReference);
        }

        public static void UnregisterGlobalUniform(string _uniformKey)
        {
            globalUniforms.Remove(_uniformKey);
        }

        /// <summary>
        /// Updates all shaders with global uniform data.
        /// </summary>
        public static void UpdateShaders()
        {
            // Calculating if each uniform is dirty
            foreach (var uniform in globalUniforms)
            {
                uniform.Value.CheckDirty();
            }

            // Setting these new values
            for (int i = 0; i < shaders.Count; i++)
            {
                UpdateShader(shaders[i]);
            }

            // Resetting the dirty flag in each uniform
            foreach (var uniform in globalUniforms)
            {
                uniform.Value.IsDirty = false;
            }
        }

        /// <summary>
        /// Updates an individual shader with global uniform data.
        /// </summary>
        public static void UpdateShader(Shader _shader)
        {
            _shader.Use();
            if(_shader.GlobalUniformTags != null)
            {
                for (int x = 0; x < _shader.GlobalUniformTags.Length; x++)
                {
                    SetUniform(_shader.GlobalUniformTags[x], _shader);
                }
            }
        }

        /// <summary>
        /// Sets the uniform data from an IGlobalUniform to a shader.
        /// </summary>
        private static void SetUniform(string _uniformKey, Shader _shader)
        {
            if(globalUniforms.TryGetValue(_uniformKey, out IGlobalUniform uniform))
            {
                if(uniform.IsDirty)
                {
                    uniform.ApplyUniform(_shader);
                }
            }
            else
            {
                Debug.LogError($"Global uniform does not exist: {_uniformKey}");
            }
        }
    }
}
