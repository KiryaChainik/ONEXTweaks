// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.IO;
using System.Numerics;

namespace XboxGamingBar
{
    // Reads the quantized mesh and the two projected textures from the approved
    // viewer. No browser, graphics packages or per-frame mesh allocations.
    internal sealed class OnexGyroConsoleMesh
    {
        private const int TextureWidth = 640;
        private const int TextureHeight = 300;
        private readonly Vector3[] points, normals;
        private readonly ushort[] triangles;
        private readonly uint[] front, rear;
        private readonly OnexApprovedTheme theme;
        private readonly ProjectedVertex[] projected;
        private readonly float[] depth;
        private readonly uint[] colors;

        private struct ProjectedVertex
        {
            internal float X, Y, Depth, TexX, TexY, TexZ, Front, Rear, Light, Rim;
        }

        internal const int Width = 552;
        internal const int Height = 260;
        internal readonly byte[] Pixels = new byte[Width * Height * 4];

        internal OnexGyroConsoleMesh(Stream input, OnexApprovedTheme theme = null)
        {
            this.theme = theme;
            using (var reader = new BinaryReader(input))
            {
                if (reader.ReadUInt32() != 0x3347584F) throw new InvalidDataException("Unknown console model");
                int meshLength = reader.ReadInt32();
                int frontLength = reader.ReadInt32();
                int rearLength = reader.ReadInt32();
                int vertices = reader.ReadUInt16();
                int faces = reader.ReadUInt16();
                if (vertices < 3 || faces < 1 || meshLength != 4 + vertices * 10 + faces * 6 ||
                    frontLength < 4 || rearLength < 4 || frontLength > 250000 || rearLength > 250000)
                    throw new InvalidDataException("Invalid console model length");

                points = new Vector3[vertices];
                normals = new Vector3[vertices];
                for (int i = 0; i < vertices; i++)
                {
                    points[i] = new Vector3(reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()) * .005f;
                    normals[i] = new Vector3(reader.ReadSByte(), reader.ReadSByte(), reader.ReadSByte()) / 127;
                    reader.ReadByte(); // alignment
                }
                triangles = new ushort[faces * 3];
                for (int i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = reader.ReadUInt16();
                    if (triangles[i] >= vertices) throw new InvalidDataException("Invalid console model triangle");
                }
                front = ReadTexture(reader.ReadBytes(frontLength));
                rear = ReadTexture(reader.ReadBytes(rearLength));
            }
            projected = new ProjectedVertex[points.Length];
            depth = new float[Width * Height];
            colors = new uint[Width * Height];
        }

        private uint[] ReadTexture(byte[] data)
        {
            if (data.Length < 4 || data[0] == 0 || data[0] > 224 || data.Length < 1 + data[0] * 3)
                throw new InvalidDataException("Invalid console texture palette");
            var palette = new uint[data[0]];
            for (int i = 0; i < palette.Length; i++)
            {
                int at = 1 + i * 3;
                int red = data[at], green = data[at + 1], blue = data[at + 2];
                if (theme != null)
                {
                    double luma = .2126 * red + .7152 * green + .0722 * blue;
                    bool mark = blue > red + 22 && luma > 65;
                    double weight = Math.Max(0, Math.Min(1, (luma - 20) / 150));
                    var color = OnexApprovedTheme.Blend(theme.Surface,
                        mark ? theme.Accent(OnexThemeRole.Controller) : theme.DiagramOutline, weight);
                    red = color.R; green = color.G; blue = color.B;
                }
                palette[i] = (uint)(red | green << 8 | blue << 16);
            }
            var pixels = new uint[TextureWidth * TextureHeight];
            int written = 0;
            for (int i = 1 + palette.Length * 3; i < data.Length; i++)
            {
                int index = data[i], count = 1;
                if (index >= 224)
                {
                    count = index - 221;
                    if (++i == data.Length) throw new InvalidDataException("Truncated console texture");
                    index = data[i];
                }
                if (index >= palette.Length || count > pixels.Length - written)
                    throw new InvalidDataException("Invalid console texture run");
                uint pixel = palette[index];
                for (int end = written + count; written < end; written++) pixels[written] = pixel;
            }
            if (written != pixels.Length) throw new InvalidDataException("Incomplete console texture");
            return pixels;
        }

        private static float Saturate(float v) => Math.Max(0, Math.Min(1, v));

        internal void Render(Quaternion orientation)
        {
            Array.Clear(depth, 0, depth.Length);
            Array.Clear(colors, 0, colors.Length);
            Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(orientation);
            float extentX = 0, extentY = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i], n = normals[i];
                Vector3 rotated = Vector3.Transform(new Vector3(p.X, p.Y - 3, p.Z + 21) / 158, rotation);
                Vector3 normal = Vector3.TransformNormal(n, rotation);
                float inverse = 1 / (3.7f - rotated.Z);
                float x = rotated.X * inverse, y = -rotated.Y * inverse;
                extentX = Math.Max(extentX, Math.Abs(x));
                extentY = Math.Max(extentY, Math.Abs(y));
                float key = Math.Max(0, -.39f * normal.X + .58f * normal.Y + .71f * normal.Z);
                float fill = Math.Max(0, .56f * normal.X - .28f * normal.Y + .42f * normal.Z);
                float grazing = 1 - Saturate(normal.Z);

                projected[i] = new ProjectedVertex
                {
                    X = x, Y = y, Depth = inverse,
                    TexX = p.X * inverse, TexY = p.Y * inverse, TexZ = p.Z * inverse,
                    Front = Saturate((n.Z + .14f) / .66f) * Saturate((p.Z + 28) / 14),
                    Rear = Saturate((-n.Z + .10f) / .68f) * Saturate((-p.Z - 11) / 13),
                    Light = .63f + .56f * key + .17f * fill,
                    Rim = grazing * grazing * (5 + 12 * Math.Max(0, .1f + .65f * normal.Y - .48f * normal.X))
                };
            }

            // Fit a full roll in the viewport while keeping shallow rotations
            // at a constant camera scale.
            float scale = Math.Min(Width * 1.58f,
                Math.Min(Width * .445f / extentX, Height * .418f / extentY));
            for (int i = 0; i < projected.Length; i++)
            {
                projected[i].X = projected[i].X * scale + Width / 2f;
                projected[i].Y = projected[i].Y * scale + Height * .494f;
            }
            for (int i = 0; i < triangles.Length; i += 3)
                PaintTriangle(projected[triangles[i]], projected[triangles[i + 1]], projected[triangles[i + 2]]);

            // WriteableBitmap reads premultiplied BGRA8. Every pixel is opaque
            // model color or fully transparent.
            Buffer.BlockCopy(colors, 0, Pixels, 0, Pixels.Length);
        }

        private void PaintTriangle(ProjectedVertex a, ProjectedVertex b, ProjectedVertex c)
        {
            float ux = b.X - a.X, uy = b.Y - a.Y, vx = c.X - a.X, vy = c.Y - a.Y;
            float area = ux * vy - uy * vx;
            if (area >= -.001f) return;
            float invArea = 1 / area;
            float ds = vy * invArea, dt = -uy * invArea;
            int left = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))));
            int right = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
            int top = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
            int bottom = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
            float dx = left + .5f - a.X;
            for (int row = top; row <= bottom; row++)
            {
                float dy = row + .5f - a.Y;
                float s = (dx * vy - dy * vx) * invArea;
                float t = (ux * dy - uy * dx) * invArea;
                for (int column = left, index = row * Width + left; column <= right; column++, index++, s += ds, t += dt)
                {
                    float u = 1 - s - t;
                    if (u < 0 || s < 0 || t < 0) continue;
                    float z = u * a.Depth + s * b.Depth + t * c.Depth;
                    if (z <= depth[index]) continue;
                    depth[index] = z;
                    colors[index] = Shade(a, b, c, u, s, t, z);
                }
            }
        }

        private uint Shade(ProjectedVertex a, ProjectedVertex b, ProjectedVertex c, float u, float s, float t, float z)
        {
            float x = (u * a.TexX + s * b.TexX + t * c.TexX) / z;
            float y = (u * a.TexY + s * b.TexY + t * c.TexY) / z;
            int tx = Math.Max(0, Math.Min(TextureWidth - 1, (int)(320 + 2 * x)));
            int ty = Math.Max(0, Math.Min(TextureHeight - 1, (int)(150 - 2 * y)));
            int offset = ty * TextureWidth;
            uint f = front[offset + tx], r = rear[offset + TextureWidth - 1 - tx];
            float frontMix = u * a.Front + s * b.Front + t * c.Front;
            float rearMix = u * a.Rear + s * b.Rear + t * c.Rear;
            float shell = 1 - frontMix - rearMix;
            float red = (f & 255) * frontMix + (r & 255) * rearMix + (theme?.Raised.R ?? 29) * shell;
            float green = (f >> 8 & 255) * frontMix + (r >> 8 & 255) * rearMix + (theme?.Raised.G ?? 43) * shell;
            float blue = (f >> 16) * frontMix + (r >> 16) * rearMix + (theme?.Raised.B ?? 58) * shell;
            if (y < -62.9f && Math.Abs(x) < 11 && shell > .62f)
            {
                float localZ = (u * a.TexZ + s * b.TexZ + t * c.TexZ) / z;
                if (Math.Abs(localZ + 12.8f) < 2.9f)
                {
                    float pin = (x + 8.5f) / 3.35f;
                    bool contact = Math.Abs(pin - Math.Round(pin)) < .17 && Math.Abs(x) < 8 && Math.Abs(localZ + 12.8f) < .94f;
                    red = contact ? 154 : 19;
                    green = contact ? 122 : 29;
                    blue = contact ? 84 : 42;
                }
            }
            float light = u * a.Light + s * b.Light + t * c.Light;
            if (theme?.Light == true) light = .85f + light * .15f;
            float rim = u * a.Rim + s * b.Rim + t * c.Rim;
            uint rr = (uint)Math.Min(255, red * light + rim + .5f);
            uint gg = (uint)Math.Min(255, green * light + rim * 1.19f + .5f);
            uint bb = (uint)Math.Min(255, blue * light + rim * 1.48f + .5f);
            return 0xff000000 | rr << 16 | gg << 8 | bb;
        }
    }
}
