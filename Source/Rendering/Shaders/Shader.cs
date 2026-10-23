using static Atlas2D.OpenGL.GL;
using System.Numerics;


namespace Atlas2D.Rendering.Shaders
{
    public class Shader : IDisposable
    {
        public uint ProgramID { get; private set; }
        public bool Compiled { get; private set; }
        public string[] GlobalUniformTags { get; private set; }
        public string FilePath => shaderProgramSource.FilePath;

        private readonly IDictionary<string, int> localUniforms = new Dictionary<string, int>();
        private ShaderProgramSource shaderProgramSource { get; }

        public Shader(ShaderProgramSource shaderProgramSource, bool compile = false, string[] globalUniformTags = null)
        {
            this.shaderProgramSource = shaderProgramSource;
            GlobalUniformTags = globalUniformTags;
            if (compile) Compile();
        }

        public static ShaderProgramSource ParseShader(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError("Shader does not exist: " + filePath);
                return null;
            }

            string[] shaderSource = new string[2];
            ShaderType shaderType = ShaderType.NONE;

            foreach (var line in File.ReadAllLines(filePath))
            {
                if (line.ToLower().Contains("#shader"))
                {
                    if (line.ToLower().Contains("vertex"))
                        shaderType = ShaderType.VERTEX;
                    else if (line.ToLower().Contains("fragment"))
                        shaderType = ShaderType.FRAGMENT;
                    else
                        Debug.LogError("No shader type has been supplied for: " + filePath);
                }
                else
                {
                    shaderSource[(int)shaderType] += line + Environment.NewLine;
                }
            }

            return new ShaderProgramSource(filePath, shaderSource[(int)ShaderType.VERTEX], shaderSource[(int)ShaderType.FRAGMENT]);
        }

        public unsafe bool Compile()
        {
            if (Compiled)
            {
                Debug.LogError("Trying to compile shader when it has already been compiled.");
                return false;
            }

            if (shaderProgramSource == null)
                throw new InvalidOperationException("Could not load shader. Source is null.");

            uint vs = glCreateShader(GL_VERTEX_SHADER);
            glShaderSource(vs, shaderProgramSource.VertexShaderSource);
            glCompileShader(vs);

            if (glGetShaderiv(vs, GL_COMPILE_STATUS, 1)[0] == 0)
            {
                Debug.LogError("Vertex Shader: " + glGetShaderInfoLog(vs));
                glDeleteShader(vs);
                return false;
            }

            uint fs = glCreateShader(GL_FRAGMENT_SHADER);
            glShaderSource(fs, shaderProgramSource.FragmentShaderSource);
            glCompileShader(fs);

            if (glGetShaderiv(fs, GL_COMPILE_STATUS, 1)[0] == 0)
            {
                Debug.LogError("Fragment Shader: " + glGetShaderInfoLog(fs));
                glDeleteShader(vs);
                glDeleteShader(fs);
                return false;
            }

            ProgramID = glCreateProgram();
            glAttachShader(ProgramID, vs);
            glAttachShader(ProgramID, fs);
            glLinkProgram(ProgramID);

            glDetachShader(ProgramID, vs);
            glDetachShader(ProgramID, fs);
            glDeleteShader(vs);
            glDeleteShader(fs);

            if (glGetProgramiv(ProgramID, GL_LINK_STATUS, 1)[0] == 0)
            {
                Debug.LogError("Shader Program: " + glGetProgramInfoLog(ProgramID));
                glDeleteProgram(ProgramID);
                ProgramID = 0;
                return false;
            }

            Compiled = true;
            ShaderGlobalUniforms.RegisterShader(this);
            return true;
        }

        public void Use()
        {
            if (!Compiled)
                throw new InvalidOperationException("Shader has not been compiled yet, cannot use.");
            glUseProgram(ProgramID);
        }

        private int GetUniformLocation(string uniformName)
        {
            if (!localUniforms.TryGetValue(uniformName, out int location))
            {
                location = glGetUniformLocation(ProgramID, uniformName);
                localUniforms.Add(uniformName, location);
            }
            return location;
        }

        public void SetMatrix4x4(string uniformName, Matrix4x4 mat)
        {
            glUniformMatrix4fv(GetUniformLocation(uniformName), 1, false, GetMatrix4x4Values(mat));
        }

        public void SetInt(string uniformName, int value)
        {
            glUniform1i(GetUniformLocation(uniformName), value);
        }

        public void SetFloat(string uniformName, float value)
        {
            glUniform1f(GetUniformLocation(uniformName), value);
        }

        public void SetVector2(string uniformName, Vector2 value)
        {
            glUniform2f(GetUniformLocation(uniformName), value.X, value.Y);
        }

        public void SetVector3(string uniformName, Vector3 value)
        {
            glUniform3f(GetUniformLocation(uniformName), value.X, value.Y, value.Z);
        }

        public void SetVector4(string uniformName, Vector4 value)
        {
            glUniform4f(GetUniformLocation(uniformName), value.X, value.Y, value.Z, value.W);
        }

        public void SetColor(string uniformName, Color value)
        {
            glUniform4f(GetUniformLocation(uniformName),
                MathF.Pow(value.R, 2.2f),
                MathF.Pow(value.G, 2.2f),
                MathF.Pow(value.B, 2.2f),
                value.A);
        }

        public void Dispose()
        {
            ShaderGlobalUniforms.UnregisterShader(this);
            if (Compiled) glDeleteProgram(ProgramID);
        }

        private float[] GetMatrix4x4Values(Matrix4x4 m) => new float[]
        {
            m.M11, m.M12, m.M13, m.M14,
            m.M21, m.M22, m.M23, m.M24,
            m.M31, m.M32, m.M33, m.M34,
            m.M41, m.M42, m.M43, m.M44
        };
    }
}