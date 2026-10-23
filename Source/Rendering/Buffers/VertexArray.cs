using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public class VertexArray : IBuffer, IDisposable
    {
        public uint BufferID { get; }
        private bool isDisposed = false;

        public VertexArray()
        {
            BufferID = glGenVertexArray();
        }

        public unsafe void AddBuffer(VertexBuffer _vertexBuffer, BufferLayout _bufferLayout)
        {
            Bind();
            _vertexBuffer.Bind();
            var elements = _bufferLayout.GetBufferElements();
            int offset = 0;
            for (int i = 0; i < elements.Count(); i++)
            {
                var currentElement = elements[i];
                glEnableVertexAttribArray((uint)i);
                glVertexAttribPointer((uint)i, currentElement.count, currentElement.type, currentElement.normalized, _bufferLayout.GetStride(), (void*)offset);
                offset += currentElement.count * GetSizeOfVertexAttribPointerType(currentElement.type);
            }
        }

        public void Bind()
        {
            glBindVertexArray(BufferID);
        }

        public void Unbind()
        {
            glBindVertexArray(0);
        }

        public void Dispose()
        {
            if(!isDisposed)
            {
                isDisposed = true;
                glDeleteVertexArray(BufferID);
            }
        }

        private int GetSizeOfVertexAttribPointerType(int _attribType)
        {
            switch (_attribType)
            {
                case GL_UNSIGNED_BYTE:
                    return 1;
                case GL_UNSIGNED_INT:
                    return 4;
                case GL_FLOAT:
                    return 4;
                default:
                    return 0;
            }
        }
    }
}
