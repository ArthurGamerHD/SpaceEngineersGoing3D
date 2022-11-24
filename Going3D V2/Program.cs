using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        readonly Engine3D Engine;

        string LCDName = "Wide LCD Panel", SeatName = "Control Seat";

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            Engine = new Engine3D(this, GridTerminalSystem.GetBlockWithName(LCDName) as IMyTextPanel, GridTerminalSystem.GetBlockWithName(SeatName) as IMyShipController, GridTerminalSystem);
            Engine.Storage = Storage;
        }
        public void Main(string argument, UpdateType updateSource)
        {
            Engine.Main(argument, updateSource);
        }

        public class Engine3D
        {
            IMyGridTerminalSystem GridTerminalSystem;

            TimeSpan Time = new TimeSpan();
            int Frames = 0;
            int FPS = 0;
            bool Lock = true;

            public string Storage;
            public bool Debug = false, NextFrame = false;
            int Clock = 0;
            IMyTextSurface surface; IMyShipController controller; RectangleF viewport;
            Camera WorldCamera = new Camera();
            IMyGridProgramRuntimeInfo Runtime;
            float FOV, AtanFOV, Zoom;
            List<IEnumerable<bool>> TaskList = new List<IEnumerable<bool>>();
            public List<MySprite> FrameBuffer = new List<MySprite>();
            public List<int> Instructions;
            IEnumerator<bool> current_work;
            string DBText;
            Program program;
            public Engine3D(Program _program, IMyTextPanel _surface, IMyShipController _Controller, IMyGridTerminalSystem _GridTerminalSystem)
            {
                TaskList.Add(ObjParse());
                program = _program;
                Runtime = program.Runtime;
                surface = _surface;
                controller = _Controller;
                GridTerminalSystem = _GridTerminalSystem;
                viewport = new RectangleF((surface.TextureSize - surface.SurfaceSize) / 2f, surface.SurfaceSize);
                surface.ContentType = ContentType.SCRIPT;
                int X = (int)surface.TextureSize.X;
                int Y = (int)surface.TextureSize.Y;
                viewport = new RectangleF(0, 0, X, Y);
                Meshes = new List<ObjectMesh>
                { DemoObject() };
                Instructions = new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                Zoom = 100;
                float Fov = 70;
                float AtanFov = (float)Math.Atan(FOV);
            }
            public void Main(string argument, UpdateType updateSource)
            {

                //if (argument == "Up") TaskList.Add(Meshes.Last().Upscale());
                if (argument == "Debug") Debug = !Debug;
                if (argument == "Demo") Meshes[0] = DemoObject();
                if (argument == "Load") TaskList.Add(ObjParse());

                if (Clock % 10 == 0) NextFrame = true; Clock++;
                DBText = $"Clock:{Clock}\nTasks:{TaskList.Count()} {(TaskList.Count() > 7 ? "\nWarning: CPU Overload,\nInput Lag & Frame Drop Expected" : "")}\n{Meshes.Last().Triangles.Count()}\nCamera:\n{WorldCamera.Position}\n/{WorldCamera.Orientation}";
                program.Echo(DBText);

                while (Runtime.CurrentInstructionCount < 40000 && TaskList.Count != 0)
                {
                    try
                    {
                        if (current_work == null)
                            current_work = TaskList[0].GetEnumerator();
                        if (current_work.MoveNext() == false)
                        {
                            current_work.Dispose();
                            current_work = null;
                            TaskList.RemoveAt(0);
                        }

                    }
                    catch (Exception Error) { program.Me.CustomData = Error.ToString(); }
                }
                if (TaskList.Count < 8)
                {
                    foreach (ObjectMesh Mesh in Meshes)
                    {
                        if (controller.RotationIndicator != new Vector2() || controller.RollIndicator != 0)
                        {
                            if (Lock)
                            {
                                TaskList.Add(Mesh.Rotate(controller.RotationIndicator.Y * 0.01f, controller.RotationIndicator.X * -0.01f, controller.RollIndicator * 0.01f));
                            }
                            else
                            {
                                WorldCamera.Rotate(new Vector3(controller.RotationIndicator.Y * 0.01f, controller.RotationIndicator.X * -0.01f, controller.RollIndicator * 0.01f));
                            }
                        }
                        if (controller.MoveIndicator != new Vector3()) { WorldCamera.Move(controller.MoveIndicator); TaskList.Add(Mesh.Offset(WorldCamera.Position)); }
                        if (FrameBuffer.Count() == 0) TaskList.Add(Render());
                    }
                }
                Instructions.RemoveAt(0);
                Instructions.Add(Runtime.CurrentInstructionCount);
                var Max = Instructions[0];
                foreach (int I in Instructions) Max = Math.Max(Max, I);
                program.Echo($"{Max}");
            }
            public IEnumerable<bool> Render()
            {
                var Center = viewport.Center;
                int Timer = 0;
                var _frame = new List<MySprite>();

                foreach (ObjectMesh Mesh in Meshes)
                {
                    foreach (Triangle Triangle in Mesh.Triangles)
                    {
                        Triangle.Update2D(WorldCamera, Zoom, Center);
                        Triangle.UpdateLines();
                        Triangle.UpdateNormal();
                        if (Triangle.Normal > 0)
                        {
                            var a = Triangle.Vectors[0];
                            var b = Triangle.Vectors[1];
                            var c = Triangle.Vectors[2];
                            var A = Triangle.Projection2D[0];
                            var B = Triangle.Projection2D[1];
                            var C = Triangle.Projection2D[2];
                            if (Debug)
                            {
                                foreach (MyTuple<Vector2, Vector2> line in Triangle.Lines)
                                    _frame.Add(DrawLine(line, 2f, Color.Green));
                                program.Me.CustomData += $"Triangle: {Triangle.Normal}:{A} / {a}\n{B} / {b}\n{C} / {c}\n\n";
                            }
                            Triangle.UpdateFaces();
                            foreach (MySprite Sprite in Triangle.Face) { _frame.Add(Sprite); }

                            if (Debug)
                            {
                                _frame.Add(new MySprite(SpriteType.TEXTURE, $"Circle", A, new Vector2(5, 5), Color.Red, "Debug", TextAlignment.CENTER));
                                _frame.Add(new MySprite(SpriteType.TEXTURE, $"Circle", B, new Vector2(5, 5), Color.Red, "Debug", TextAlignment.CENTER));
                                _frame.Add(new MySprite(SpriteType.TEXTURE, $"Circle", C, new Vector2(5, 5), Color.Red, "Debug", TextAlignment.CENTER));
                            }
                        }
                        if (Timer % 1000 == 0) yield return true;
                    }
                }
                if (Time < TimeSpan.FromTicks(DateTime.Now.Ticks) - TimeSpan.FromSeconds(1))
                {
                    FPS = Frames;
                    Frames = 0;
                    Time = TimeSpan.FromTicks(DateTime.Now.Ticks);
                }
                Frames++;

                _frame.Add(new MySprite(SpriteType.TEXT, $"FPS:{FPS}\n{DBText}", new Vector2(5, 5), null, Color.Red, "Debug", TextAlignment.LEFT, 1f));

                { //Just ignore, Glitch to bypass the 6fps limit on the LCD screen
                    surface.ContentType = ContentType.TEXT_AND_IMAGE;
                    surface.ContentType = ContentType.SCRIPT;
                }

                var Frame = surface.DrawFrame();
                Frame.AddRange(_frame);
                Frame.Dispose();
            }


            MySprite DrawLine(MyTuple<Vector2, Vector2> Points, float width, Color color)
            {
                Vector2 position = 0.5f * (Points.Item1 + Points.Item2);
                Vector2 diff = Points.Item1 - Points.Item2;
                float length = diff.Length();
                if (length > 0)
                    diff /= length;
                Vector2 size = new Vector2(length, width);
                float angle = (float)Math.Acos(Vector2.Dot(diff, Vector2.UnitX));
                angle *= Math.Sign(Vector2.Dot(diff, Vector2.UnitY));
                MySprite sprite = MySprite.CreateSprite("SquareSimple", position, size);
                sprite.RotationOrScale = angle;
                sprite.Color = color;
                return sprite;
            }


            public class Camera
            {
                public Vector3 Position = new Vector3();
                public Vector3 Orientation = new Vector3();
                public Matrix Translation;
                public Matrix Perspective;

                public Camera()
                {
                    Position = new Vector3(0, 0, 10);
                    Perspective = Matrix.CreatePerspectiveFieldOfView(1.05f, 1, 1, 800);
                    SetMatrix();
                }
                void SetMatrix()
                {
                    var Up = new Vector3(0, 1, 0);
                    Translation = Matrix.CreateLookAtInverse(Position, Orientation, Up);
                }

                public void Rotate(Vector3 Vector)
                {
                    Orientation += Vector * .1f;

                    if (Orientation.X > 1.57 || Orientation.X < -1.57) Orientation.X *= -1;
                    if (Orientation.Y > 1.57 || Orientation.Y < -1.57) Orientation.Y *= -1;
                    if (Orientation.Z > 1.57 || Orientation.Z < -1.57) Orientation.Z *= -1;

                    SetMatrix();
                }
                public void Move(Vector3 Vector)
                {
                    var Rotate = Matrix.CreateFromYawPitchRoll(Orientation.X, Orientation.Y, Orientation.Z);
                    var VectorTransformed = Vector3.Transform(Vector, Rotate);
                    Position += VectorTransformed;

                }
            }

            public class Triangle
            {
                public List<Vector3> Vectors = new List<Vector3>();
                public List<Vector3> ProjectedVec = new List<Vector3>();
                public List<Vector2> Projection2D = new List<Vector2>();
                public Vector3 Offset = new Vector3();
                public List<MySprite> Face = new List<MySprite>();
                public Vector3 FaceColor;
                public List<MyTuple<Vector2, Vector2>> Lines;
                public float Normal;
                public Camera Cam;

                public Triangle(List<Vector3> V) { Vectors = V; FaceColor = Color.White.ToVector3(); }

                public void Rotate(float X, float Y, float Z)
                {
                    var M = Matrix.CreateFromYawPitchRoll(X, Y, Z);
                    Vectors[0] = Vector3.Transform(Vectors[0], M);
                    Vectors[1] = Vector3.Transform(Vectors[1], M);
                    Vectors[2] = Vector3.Transform(Vectors[2], M);
                }
                public void Update2D(Camera _Cam, float S = 1, Vector2 offset = new Vector2())
                {
                    Cam = _Cam;
                    Projection2D.Clear();
                    ProjectedVec.Clear();

                    foreach (Vector3 V in Vectors)
                    {
                        var v3 = MathProject(V + Offset, Cam.Perspective);
                        //v3 = MathProject(v3, Cam.Translation);
                        //var v3 = MathProject(V + Cam.Position, Cam.Perspective);
                        ProjectedVec.Add(v3);
                        Projection2D.Add(new Vector2(v3.X, v3.Y) * S + offset);
                    }
                }
                public void UpdateLines()
                {
                    if (Projection2D.Count != 3) { Update2D(new Camera()); }
                    else
                        Lines = new List<MyTuple<Vector2, Vector2>> {
                new MyTuple<Vector2, Vector2>(Projection2D[0], Projection2D[1]),
                new MyTuple<Vector2, Vector2>(Projection2D[1], Projection2D[2]),
                new MyTuple<Vector2, Vector2>(Projection2D[2], Projection2D[0])
            };
                }
                public void UpdateNormal(Vector3 vCamera = new Vector3())
                {

                    Vector3 normal, line1, line2;
                    line1.X = ProjectedVec[1].X - ProjectedVec[0].X;
                    line1.Y = ProjectedVec[1].Y - ProjectedVec[0].Y;
                    line1.Z = ProjectedVec[1].Z - ProjectedVec[0].Z;

                    line2.X = ProjectedVec[2].X - ProjectedVec[0].X;
                    line2.Y = ProjectedVec[2].Y - ProjectedVec[0].Y;
                    line2.Z = ProjectedVec[2].Z - ProjectedVec[0].Z;

                    normal.X = line1.Y * line2.Z - line1.Z * line2.Y;
                    normal.Y = line1.Z * line2.X - line1.X * line2.Z;
                    normal.Z = line1.X * line2.Y - line1.Y * line2.X;

                    var norm = normal.Normalize();
                    normal.X /= norm; normal.Y /= norm; normal.Z /= norm;

                    Normal = (normal.X * (ProjectedVec[0].X - vCamera.X) + normal.Y * (ProjectedVec[0].Y - vCamera.Y) + normal.Z * (ProjectedVec[0].Z - vCamera.Z));
                }

                public void UpdateFaces()
                {
                    var sq0 = (Projection2D[1] - Projection2D[2]).LengthSquared();
                    var sq1 = (Projection2D[0] - Projection2D[2]).LengthSquared();
                    var sq2 = (Projection2D[0] - Projection2D[1]).LengthSquared();
                    if (sq0 > sq1 + sq2)
                    {
                        DrawTriangle(1, 2, 0);
                    }
                    else if (sq1 > sq0 + sq2)
                    {
                        DrawTriangle(2, 0, 1);
                    }
                    else // l2 > l0 + l1
                    {
                        DrawTriangle(0, 1, 2);
                    }
                }
                private void DrawTriangle(int A_, int B_, int C_)
                {

                    Face.Clear();
                    var Distance = ((ProjectedVec[A_] + ProjectedVec[B_] + ProjectedVec[C_]) / 3).Length();
                    var A = Projection2D[A_];
                    var B = Projection2D[B_];
                    var C = Projection2D[C_];

                    if (A == C || B == C)
                    {
                        return;
                    }
                    var AC = C - A;
                    var BA = A - B;
                    var D = A + (Vector2.Dot(AC, BA) / Vector2.Dot(BA, BA)) * BA;
                    var h = (C - D).Length();
                    var a = (A - D).Length();
                    var b = (B - D).Length();
                    var T1 = (C + A) / 2;
                    var T2 = (C + B) / 2;
                    float angle = (float)Math.Atan2(BA.Y, BA.X);
                    var t1 = new Vector2(-a, h) / 2;

                    float sin = (float)Math.Sin(angle);
                    float cos = (float)Math.Cos(angle);
                    var ActualD = new Vector2(cos * t1.X - sin * t1.Y, sin * t1.X + cos * t1.Y) + T1;
                    if ((ActualD - D).Length() > 0.1f)
                    {
                        h = -h;
                    }
                    Face.Add(new MySprite(
                        SpriteType.TEXTURE,
                        "RightTriangle",
                        T1,
                        new Vector2(a, h) * 1.02f,
                        FaceColor / (Distance * 5),
                        rotation: angle
                        ));
                    Face.Add(new MySprite(
                        SpriteType.TEXTURE,
                        "RightTriangle",
                        T2,
                        new Vector2(-b, h) * 1.02f,
                        FaceColor / (Distance * 5),
                        rotation: angle
                        ));
                }
                public Vector3 MathProject(Vector3 vector, Matrix _Matrix)
                {
                    return Vector3.Transform(vector, _Matrix);
                }

                public void Scale(float Size)
                {
                    Vectors = new List<Vector3> { Vectors[0] * Size, Vectors[1] * Size, Vectors[2] * Size };
                }
                public static Triangle New(float xa, float ya, float za, float xb, float yb, float zb, float xc, float yc, float zc)
                { return new Triangle(new List<Vector3> { new Vector3(xa, ya, za), new Vector3(xb, yb, zb), new Vector3(xc, yc, zc) }); }
            }
            public class ObjectMesh
            {
                public string Name;
                public Dictionary<int, Vector3> Vertices = new Dictionary<int, Vector3>();
                public List<KeyValuePair<int, int>> Lines = new List<KeyValuePair<int, int>>();
                public List<Triangle> Triangles;
                public ObjectMesh(List<Triangle> t) { Triangles = t; }
                public IEnumerable<bool> Scale(float Size)
                {
                    for (int I = 0; I < Triangles.Count(); I++) { Triangles[I].Scale(Size); if (I % 1000 == 0) yield return true; }
                }
                public IEnumerable<bool> Offset(Vector3 Vector)
                {
                    for (int I = 0; I < Triangles.Count(); I++) { Triangles[I].Offset = Vector; if (I % 1000 == 0) yield return true; }
                }
                public IEnumerable<bool> Rotate(float X, float Y, float Z)
                {
                    for (int I = 0; I < Triangles.Count(); I++) { Triangles[I].Rotate(X, Y, Z); if (I % 1000 == 0) yield return true; }
                }
                public IEnumerable<bool> Upscale()
                {
                    yield return true;
                }

            }
            readonly List<ObjectMesh> Meshes;

            public ObjectMesh DemoObject()
            {
                return new ObjectMesh(new List<Triangle>
                {
            // SOUTH
            Triangle.New( -1.0f, -1.0f, -1.0f,    -1.0f, 1.0f, -1.0f,    1.0f, 1.0f, -1.0f ),
            Triangle.New( -1.0f, -1.0f, -1.0f,    1.0f, 1.0f, -1.0f,    1.0f, -1.0f, -1.0f ),

            // EAST
            Triangle.New( 1.0f, -1.0f, -1.0f,    1.0f, 1.0f, -1.0f,    1.0f, 1.0f, 1.0f ),
            Triangle.New( 1.0f, -1.0f, -1.0f,    1.0f, 1.0f, 1.0f,    1.0f, -1.0f, 1.0f ),

            // NORTH
            Triangle.New( 1.0f, -1.0f, 1.0f,    1.0f, 1.0f, 1.0f,    -1.0f, 1.0f, 1.0f ),
            Triangle.New( 1.0f, -1.0f, 1.0f,    -1.0f, 1.0f, 1.0f,    -1.0f, -1.0f, 1.0f ),

            // WEST
            Triangle.New( -1.0f, -1.0f, 1.0f,    -1.0f, 1.0f, 1.0f,    -1.0f, 1.0f, -1.0f ),
            Triangle.New( -1.0f, -1.0f, 1.0f,    -1.0f, 1.0f, -1.0f,    -1.0f, -1.0f, -1.0f ),

            // TOP
            Triangle.New( -1.0f, 1.0f, -1.0f,    -1.0f, 1.0f, 1.0f,    1.0f, 1.0f, 1.0f ),
            Triangle.New( -1.0f, 1.0f, -1.0f,    1.0f, 1.0f, 1.0f,    1.0f, 1.0f, -1.0f ),

            // BOTTOM
            Triangle.New( 1.0f, -1.0f, 1.0f,    -1.0f, -1.0f, 1.0f,    -1.0f, -1.0f, -1.0f ),
            Triangle.New( 1.0f, -1.0f, 1.0f,    -1.0f, -1.0f, -1.0f,    1.0f, -1.0f, -1.0f )
                });
            }

            IEnumerable<bool> ObjParse()
            {
                ObjectMesh Object3D = new ObjectMesh(new List<Triangle>());
                string RawData = "";
                string[] ObjectString;
                if (Storage == "")
                {
                    for (int i = 1; true; i++)
                    {
                        var block = GridTerminalSystem.GetBlockWithName($"Memory Card {i}");
                        if (block == null) break;
                        RawData += block.CustomData;
                    }
                    ObjectString = RawData.Split('\n');
                }
                else ObjectString = Storage.Split('\n');
                List<string> Vertices = new List<string>();
                List<string> VertAttr = new List<string>();
                List<string> Faces = new List<string>();
                List<string> Line = new List<string>();
                List<string> Points = new List<string>();
                List<KeyValuePair<int, int>> _MyLines = new List<KeyValuePair<int, int>>();
                foreach (string MyObject in ObjectString)
                {
                    if (MyObject.StartsWith("vn")) VertAttr.Add(MyObject.Replace("vn ", ""));
                    else if (MyObject.StartsWith("f ")) Faces.Add(MyObject.Replace("f ", ""));
                    else if (MyObject.StartsWith("p ")) Points.Add(MyObject.Replace("p ", ""));
                    else if (MyObject.StartsWith("v ")) Vertices.Add(MyObject.Replace("v ", ""));
                    else if (MyObject.StartsWith("l ")) Vertices.Add(MyObject.Replace("l ", ""));
                    else if (MyObject.StartsWith("mtllib")) Object3D.Name = MyObject.Split(' ')[1];
                }
                int Yield = 0;
                for (var i = 1; i <= Vertices.Count; i++)
                {
                    var VerticeComponent = Vertices[i - 1].Split(' ');
                    List<float> Cordinates = new List<float>();
                    Yield++;
                    foreach (string Point in VerticeComponent)
                    {
                        float fPoint;
                        if (float.TryParse(Point, out fPoint))
                            Cordinates.Add(fPoint);
                    }
                    if (Cordinates.Count == 3)
                        Object3D.Vertices.Add(i, new Vector3(Cordinates[0], Cordinates[1], Cordinates[2]));
                    if (Yield % 100 == 0) yield return true;
                }
                foreach (string Face in Faces)
                {
                    List<int> I = new List<int>();
                    Yield++;
                    var Raw = Face.Split(' ');
                    foreach (string IntLine in Raw)
                    {
                        int i;
                        if (IntLine.Contains('/'))
                        {
                            if (int.TryParse(IntLine.Split('/')[0], out i))
                                I.Add(i);
                        }
                        else
                        {
                            if (int.TryParse(IntLine, out i))
                                I.Add(i);
                        }
                    }
                    if (I.Count >= 3)
                    {
                        Object3D.Triangles.Add(new Triangle(new List<Vector3> { Object3D.Vertices.GetValueOrDefault(I[2]), Object3D.Vertices.GetValueOrDefault(I[1]), Object3D.Vertices.GetValueOrDefault(I[0]) }));
                    }
                    if (I.Count == 4)
                    {
                        Object3D.Triangles.Add(new Triangle(new List<Vector3> { Object3D.Vertices.GetValueOrDefault(I[3]), Object3D.Vertices.GetValueOrDefault(I[1]), Object3D.Vertices.GetValueOrDefault(I[2]) }));
                    }
                    if (Yield % 100 == 0) yield return true;
                }
                Object3D.Scale(50f);
                Object3D.Offset(WorldCamera.Position);
                TaskList.Add(Object3D.Offset(WorldCamera.Position));
                Meshes[0] = Object3D;
                yield return true;
            }
        }
    }
}

