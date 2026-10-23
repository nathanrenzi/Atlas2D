using Box2D.NetStandard.Collision.Shapes;
using Box2D.NetStandard.Dynamics.Bodies;
using Box2D.NetStandard.Dynamics.Fixtures;
using Atlas2D.PhysicsBox2D;
using Atlas2D.Rendering;
using System.Numerics;

namespace Atlas2D
{
    public class TilemapNode : TransformNode, IRenderable
    {
        public TileData[] Data { get; set; }
        public int SizeX { get; set; }
        public int SizeY { get; set; }
        public int[] Tiles { get; set; }
        public byte[] TileRotations { get; set; }
        public uint CollisionBody { get; private set; } = 999999999;
        public Dictionary<Vector2, Fixture> CollisionFixtures { get; set; } = new();
        public Dictionary<Vector2, bool> CollisionFixtureUpdates { get; set; } = new();
        public int TilePixelSize { get; set; }
        public int RenderLayer { get; }
        public bool IgnorePostProcessing { get; } = false;

        private int _realTilePixelSize => TilePixelSize * 2;

        private Dictionary<Material, TileMesh> _meshDataDictionary = new();
        private List<TileMesh> _meshList = new();
        private Random _random;
        private int _seed;
        private bool _isDirty = false;
        private bool _isColliderDirty = false;

        private TilemapNode(TileData[] data, int[] tileArray, int tilePixelSize,
            int sizeX, int sizeY, int renderLayer = -1)
        {
            TilePixelSize = tilePixelSize;
            Data = data;
            Tiles = tileArray;
            SizeX = sizeX;
            SizeY = sizeY;
            RenderLayer = renderLayer;

            _seed = 1337 * sizeX + tilePixelSize * renderLayer;
            _random = new Random(_seed);

            for (int i = 0; i < Data.Length; i++)
            {
                if (Data[i].Material == null) continue;
                if (!_meshDataDictionary.ContainsKey(Data[i].Material.Value))
                {
                    TileMesh mesh = new TileMesh(this, Data[i].Material);
                    _meshDataDictionary.Add(Data[i].Material.Value, mesh);
                    _meshList.Add(mesh);
                }
            }

            TileRotations = new byte[Tiles.Length];
            for (int i = 0; i < Tiles.Length; i++)
                TileRotations[i] = (byte)_random.Next(0, 4);

            for (int i = 0; i < Tiles.Length; i++)
            {
                if (IsColliderTile(Tiles[i]))
                    CollisionFixtureUpdates[FromIndex(i)] = true;
            }

            _isDirty = true;
            _isColliderDirty = CollisionFixtureUpdates.Count > 0;
        }

        public static TilemapNode CreateSharedMaterial(SharedResource<Material> material, TileData[] data,
            int[] tileArray, int tilePixelSize,
            int sizeX, int sizeY, int renderLayer = -1, bool cloneArrays = true)
        {
            TileData[] d = cloneArrays ? (TileData[])data.Clone() : data;
            int[] tiles = cloneArrays ? (int[])tileArray.Clone() : tileArray;

            if (!cloneArrays)
                Debug.LogWarning("Tilemap with shared material is being created without cloning input arrays.");

            for (int i = 0; i < d.Length; i++)
                d[i].Material = material;

            return new TilemapNode(d, tiles, tilePixelSize, sizeX, sizeY, renderLayer);
        }

        public static TilemapNode CreateMultiMaterial(TileData[] data, int[] tileArray, int tilePixelSize,
            int sizeX, int sizeY, int renderLayer = -1, bool cloneArrays = true)
        {
            TileData[] d = cloneArrays ? (TileData[])data.Clone() : data;
            int[] tiles = cloneArrays ? (int[])tileArray.Clone() : tileArray;
            return new TilemapNode(d, tiles, tilePixelSize, sizeX, sizeY, renderLayer);
        }

        protected override void OnEnable()
        {
            RenderLayerManager.OrderRenderable(this);
        }

        protected override void OnDisable()
        {
            RenderLayerManager.RemoveRenderable(this);
        }

        protected override void OnDispose()
        {
            RenderLayerManager.RemoveRenderable(this);
            for (int i = 0; i < _meshList.Count; i++)
                _meshList[i].Renderer.Dispose();
            if (CollisionBody != 999999999)
                Physics.RemovePhysicsBody(CollisionBody);
        }

        protected override void OnUpdate() { RegenerateEntireMesh(); }

        public string ToJson() => throw new NotImplementedException();
        public static TilemapNode FromJson(string json) => throw new NotImplementedException();

        private void RegenerateEntireMesh()
        {
            if (!_isDirty && _isColliderDirty) RegenerateColliders();
            if (!_isDirty) return;
            _isDirty = false;

            // Clear all mesh data before rebuilding to avoid duplicate geometry
            for (int i = 0; i < _meshList.Count; i++)
            {
                _meshList[i].Vertices.Clear();
                _meshList[i].Indices.Clear();
            }

            for (int i = 0; i < Tiles.Length; i++)
            {
                if (Tiles[i] == -1) continue;
                TileData data = Data[Tiles[i]];
                TileMesh mesh = _meshDataDictionary[data.Material.Value];
                Texture2D mainTexture = data.Material.Value.MainTexture.Value;

                Vector2 pos = FromIndex(i);
                float xPos = pos.X * _realTilePixelSize;
                float yPos = pos.Y * _realTilePixelSize;

                int[] neighbors = new int[9];
                for (int y = -1; y < 2; y++)
                {
                    for (int x = -1; x < 2; x++)
                    {
                        int tileIndex = i + x + (y * SizeX);
                        if (tileIndex < Tiles.Length && tileIndex >= 0)
                        {
                            int neighborXPos = (i % SizeX) + x;
                            neighbors[(x + 1) + ((y + 1) * 3)] = (neighborXPos < 0 || neighborXPos >= SizeX)
                                ? -1
                                : Tiles[tileIndex];
                        }
                        else
                        {
                            neighbors[(x + 1) + ((y + 1) * 3)] = -1;
                        }
                    }
                }

                for (int a = 0; a < 4; a++)
                {
                    float xMod = (a == 0 || a == 3) ? _realTilePixelSize : 0;
                    float yMod = (a == 0 || a == 1) ? _realTilePixelSize : 0;

                    mesh.Vertices.Add(xPos + xMod);
                    mesh.Vertices.Add(yPos + yMod);

                    float u = xMod / _realTilePixelSize;
                    float v = yMod / _realTilePixelSize;
                    Vector2 newUV;
                    if (data.Ruleset != null)
                    {
                        newUV = data.Ruleset.CheckRulesetGetVertexUV(neighbors, new Vector2(u, v));
                    }
                    else
                    {
                        newUV = Spritesheets.spritesheets.ContainsKey(mainTexture)
                            ? Spritesheets.GetVertexUV(mainTexture, data.SpriteColumn, data.SpriteRow, new Vector2(u, v))
                            : new Vector2(u, v);

                        if (data.AllowRandomRotation)
                            newUV = RotateUV(newUV, TileRotations[i] * 90);
                    }

                    mesh.Vertices.Add(newUV.X);
                    mesh.Vertices.Add(newUV.Y);
                }

                int vertices = mesh.Vertices.Count / 4 - 4;
                mesh.Indices.Add((uint)(vertices + 0));
                mesh.Indices.Add((uint)(vertices + 1));
                mesh.Indices.Add((uint)(vertices + 2));
                mesh.Indices.Add((uint)(vertices + 0));
                mesh.Indices.Add((uint)(vertices + 2));
                mesh.Indices.Add((uint)(vertices + 3));
            }

            for (int i = 0; i < _meshList.Count; i++)
            {
                TileMesh mesh = _meshList[i];
                if (mesh.Vertices.Count > 0)
                {
                    mesh.Renderer.SetVertexArrays(mesh.Vertices.ToArray(), mesh.Indices.ToArray(),
                        !mesh.Renderer.HasVertexData, _setDirty: true);
                }
            }

            RegenerateColliders();
        }

        private void RegenerateColliders()
        {
            if (!_isColliderDirty) return;
            _isColliderDirty = false;

            // Fixtures are created on the Box2D body directly, so the physics thread can't step during this
            lock (Physics.WorldLock)
            {
                Body body = null;
                foreach (var entry in CollisionFixtureUpdates)
                {
                    Vector2 localPosition = entry.Key;
                    bool add = entry.Value;

                    if (CollisionFixtures.Remove(localPosition, out Fixture existing))
                    {
                        body ??= Physics.GetBody(CollisionBody);
                        body.DestroyFixture(existing);
                    }

                    if (add)
                    {
                        RigidbodyKinematicDef settings = Data[GetTileID((int)localPosition.X, (int)localPosition.Y)].PhysicsSettings;
                        Vector2 fixturePosition = (localPosition + (Vector2.One * 0.5f)) * _realTilePixelSize / Physics.WorldScalar;
                        PolygonShape shape = new PolygonShape();
                        shape.SetAsBox(TilePixelSize / Physics.WorldScalar, TilePixelSize / Physics.WorldScalar, fixturePosition, 0);
                        FixtureDef fdef = new FixtureDef()
                        {
                            shape = shape,
                            friction = settings.Friction,
                            restitution = settings.Bounciness,
                            filter = new Filter()
                            {
                                categoryBits = settings.Layer,
                                maskBits = settings.HitMask,
                                groupIndex = settings.GroupIndex
                            }
                        };

                        if (CollisionBody == 999999999)
                        {
                            BodyDef bodyDef = new BodyDef() { position = WorldPosition / Physics.WorldScalar };
                            uint id = Physics.CreatePhysicsBody(bodyDef, fdef, new MassData(), true);
                            if (id == uint.MaxValue) continue;
                            CollisionBody = id;
                            body = Physics.GetBody(CollisionBody);
                            CollisionFixtures.Add(localPosition, body.GetFixtureList());
                            continue;
                        }

                        body ??= Physics.GetBody(CollisionBody);
                        CollisionFixtures.Add(localPosition, body.CreateFixture(fdef));
                    }
                }
                CollisionFixtureUpdates.Clear();
            }
        }

        private Vector2 RotateUV(Vector2 uv, float degrees)
        {
            float mid = 0.5f;
            degrees *= MathF.PI / 180f;
            return new Vector2(
                MathF.Cos(degrees) * (uv.X - mid) + MathF.Sin(degrees) * (uv.Y - mid) + mid,
                MathF.Cos(degrees) * (uv.Y - mid) - MathF.Sin(degrees) * (uv.X - mid) + mid
            );
        }

        public void SetTileID(int x, int y, byte tileID)
        {
            int index = ToIndex(x, y);
            bool wasCollider = IsColliderTile(Tiles[index]);
            bool isCollider = IsColliderTile(tileID);
            Tiles[index] = tileID;
            _isDirty = true;

            if (wasCollider || isCollider)
            {
                CollisionFixtureUpdates[new Vector2(x, y)] = isCollider;
                _isColliderDirty = true;
            }
        }

        private bool IsColliderTile(int tileID) => tileID >= 0 && tileID < Data.Length && Data[tileID].IsCollider;

        public int GetTileID(int x, int y) => Tiles[ToIndex(x, y)];
        public TileData GetTileData(int x, int y) => Data[GetTileID(x, y)];
        private int ToIndex(int x, int y) => x + y * SizeX;
        private Vector2 FromIndex(int index) => new Vector2(index % SizeX, index / SizeX);

        public void Render()
        {
            for (int i = 0; i < _meshList.Count; i++)
                _meshList[i].Renderer.Render();
        }
    }
}
