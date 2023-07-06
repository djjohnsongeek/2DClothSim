using Apos.Shapes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System.Collections.Generic;
using System.IO;

namespace VerletSim
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private ShapeBatch _shapeBatch;

        public Texture2D VertexTexture;
        public Cloth Cloth;
        public int VertexRadius;
        public float Drag;
        public float Gravity;
        public float Bounce;
        public int LinkIterationCount;
        public float CutRadius;
        public float TearLength;

        public int WindowWidth => _graphics.PreferredBackBufferWidth;
        public int WindowHeight => _graphics.PreferredBackBufferHeight;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            _graphics.PreferredBackBufferWidth = 1200;
            _graphics.PreferredBackBufferHeight = 800;
            _graphics.GraphicsProfile = GraphicsProfile.HiDef;
            //_graphics.SynchronizeWithVerticalRetrace = false;
            //IsFixedTimeStep = false;
            _graphics.ApplyChanges();

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _shapeBatch = new ShapeBatch(GraphicsDevice, Content);

            var stream = new FileStream("Content/point_graphic.png", FileMode.Open);
            VertexTexture = Texture2D.FromStream(GraphicsDevice, stream);
            stream.Dispose();

            // Sim Config
            VertexRadius = 1;
            Drag = .99f;
            Gravity = .5f;
            Bounce = .30f;
            LinkIterationCount = 1;
            CutRadius = 8;
            TearLength = 68;

            // Currently cloth with more width or height than this results in some lines not being drawn
            Cloth = new Cloth(width: 105, height: 20, spacing: 11, new Vector2(8, 8));
        }

        protected override void Update(GameTime gameTime)
        {
            if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            {
                Exit();
            }

            if (Mouse.GetState().LeftButton == ButtonState.Pressed)
            {
                RemoveClosestLink(Mouse.GetState().Position.ToVector2());
            }

            UpdateVerticies(Cloth.Vertices);
            UpdateLinks(Cloth.Links);

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // Draw Links
            _shapeBatch.Begin();
            foreach (var link in Cloth.Links)
            {
                _shapeBatch.DrawLine(link.Start.Position, link.End.Position, 1, Color.White, Color.White);
            }
            _shapeBatch.End();


            // Draw Verticies
            _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, null, null, null, null);
            foreach (var vertex in Cloth.Vertices)
            {
                var color = vertex.Pinned ? Color.Gold : Color.White;

                _spriteBatch.Draw(
                    VertexTexture,
                    vertex.Position,
                    null,
                    color,
                    0,
                    new Vector2(8, 8),
                    new Vector2(.3f, .3f),
                    SpriteEffects.None,
                    0);
            }
            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private void UpdateVerticies(List<Vertex> verticies)
        {
            for (int i = 0; i < verticies.Count; i++)
            {
                Vertex vertex = verticies[i];
                if (vertex.Pinned) continue;

                Vector2 velocity = vertex.Velocity;
                velocity *= Drag;
                vertex.PrevPosition = vertex.Position;
                vertex.Position += velocity;
                vertex.Position.Y += Gravity;

                ApplyScreenContraints(vertex, velocity);
            }
        }

        private void UpdateLinks(List<Link> links)
        {
            for (int h = 0; h < LinkIterationCount; h++)
            {
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i];

                    float currentLength = link.CurrentLength;
                    Vector2 vertexDiffs = link.VertexDiffs;
                    float lengthDiff = currentLength - link.OriginalLength;

                    if (lengthDiff > TearLength)
                    {
                        Cloth.RemoveLink(i);
                        continue;
                    }

                    
                    if (link.Material == Material.Fabric && lengthDiff < 0)
                    {
                        continue;
                    }

                    // Shrink or grow link to maintain original dimensions
                    float adjustFactor = (lengthDiff / currentLength) * .5f;
                    Vector2 offset = new Vector2(vertexDiffs.X * adjustFactor, vertexDiffs.Y * adjustFactor);

                    if (!link.Start.Pinned)
                    {
                        link.Start.Position.X -= offset.X;
                        link.Start.Position.Y -= offset.Y;
                    }

                    if (!link.End.Pinned)
                    {
                        link.End.Position.X += offset.X;
                        link.End.Position.Y += offset.Y;
                    }
                }
            }
        }

        private void ApplyScreenContraints(Vertex vertex, Vector2 velocity)
        {
            
            bool collidedWithWall = false;

            // X Axis
            if (vertex.Position.X > WindowWidth - VertexRadius || vertex.Position.X < VertexRadius)
            {
                velocity.Y *= -1;
                velocity *= Bounce;
                collidedWithWall = true;
                vertex.Position.X = GetBouncedXCoord(vertex);
            }

            // Y Axis
            if (vertex.Position.Y > WindowHeight - VertexRadius || vertex.Position.Y < VertexRadius)
            {
                velocity.X *= -1;
                velocity *= Bounce;
                collidedWithWall = true;
                vertex.Position.Y = GetBouncedYCoord(vertex);
            }

            if (collidedWithWall)
            {
                vertex.PrevPosition = vertex.Position + velocity;
            }
        }

        private float GetBouncedXCoord(Vertex vertex)
        {
            if (vertex.Position.X < VertexRadius)
            {
                return VertexRadius;
            }
            else
            {
                return WindowWidth - VertexRadius;
            }
        }

        private float GetBouncedYCoord(Vertex vertex)
        {
            if (vertex.Position.Y < VertexRadius)
            {
                return VertexRadius;
            }
            else
            {
                return WindowHeight - VertexRadius;
            }
        }

        private void RemoveClosestLink(Vector2 mouseLocation)
        {
            var (index, link, distance) = GetClosestLink(mouseLocation);
            if (distance < CutRadius)
            {
                link.Start = null;
                link.End = null;
                Cloth.RemoveLink(index);
            }
        }

        private (int index, Link link, float distance) GetClosestLink(Vector2 mouseLocation)
        {
            float smallestDistance = float.MaxValue;
            Link nearistLink = null;
            int nearistIndex = 0;

            for (int i = 0; i < Cloth.Links.Count; i++)
            {
                var link = Cloth.Links[i];
                Vector2 midpoint = link.Midpoint;
                float distanceFromMouse = Vector2.Distance(mouseLocation, midpoint);

                if (distanceFromMouse < smallestDistance)
                {
                    nearistLink = link;
                    nearistIndex = i;
                    smallestDistance = distanceFromMouse;
                }
            }

            return (nearistIndex, nearistLink, smallestDistance);
        }

    }

    public class Vertex
    {
        public Vertex(Vector2 pos, Vector2 velocity, bool stationary = false)
        {
            Position = pos;
            PrevPosition = pos - velocity;
            Pinned = stationary;
        }

        public Vector2 PrevPosition;
        public Vector2 Position;
        public bool Pinned;
        public Vector2 Velocity => Position - PrevPosition;
    }

    public class Link
    {
        public Vertex Start;
        public Vertex End;
        public float OriginalLength;
        public Material Material;

        public float CurrentLength => Vector2.Distance(Start.Position, End.Position);

        public Vector2 VertexDiffs => new Vector2(Start.Position.X - End.Position.X, Start.Position.Y - End.Position.Y);

        public Vector2 Midpoint => new Vector2((Start.Position.X + End.Position.X) / 2, (Start.Position.Y + End.Position.Y) / 2);

        public Link(Vertex start, Vertex end, Material material = Material.Wood)
        {
            Start = start;
            End = end;
            OriginalLength = Vector2.Distance(Start.Position, End.Position);
            Material = material;
        }
    }

    public class Cloth
    {
        public List<Vertex> Vertices;
        public List<Link> Links;

        public Cloth(int width, int height, int spacing, Vector2 start)
        {
            Vertices = new List<Vertex>();
            Links = new List<Link>();

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var vertex = new Vertex(new Vector2(start.X + x * spacing, start.Y + y * spacing), Vector2.Zero);

                    // Link to the above vertex
                    if (y > 0)
                    {
                        var top = Vertices[Vertices.Count - 1];
                        var topLink = new Link(top, vertex, Material.Fabric);
                        Links.Add(topLink);
                    }

                    // Link to the left vertex
                    if (x > 0)
                    {
                        var left = Vertices[Vertices.Count - height];
                        var leftLink = new Link(left, vertex, Material.Fabric);
                        Links.Add(leftLink);
                    }

                    // Pin the top row
                    if (y == 0)
                    {
                        vertex.Pinned = true;
                    }

                    Vertices.Add(vertex);
                }
            }
        }

        public void RemoveLink(int index)
        {
            Links.RemoveAt(index);
        }


    }

    public enum Material
    {
        Wood,
        Fabric,
    }
}
