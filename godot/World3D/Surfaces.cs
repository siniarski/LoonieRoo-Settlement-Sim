// The world's detail textures (godot/assets/surfaces: a tileable picture per material, made by
// art/scripts/build_surfaces.py), loaded once into one texture array and handed to every shader as the global
// `surface_layers`; and the layer numbers by name. docs/textures.md is the page on this.
//
// A shader adopts them with assets/shaders/surface_detail.gdshaderinc; a mesh says which layer each face wants in
// its UV: `st.SetUV(Surfaces.What("brick"))`. Make a shader that includes it with Surfaces.Shader(code) (or call
// Surfaces.Ensure() first): a global must exist before a shader that names it is compiled.

using System;
using System.Collections.Generic;
using Godot;

namespace LoonieRoo.Game.World3D;

public static class Surfaces
{
    const string DIR = "res://assets/surfaces/";
    public const string INCLUDE = "#include \"res://assets/shaders/surface_detail.gdshaderinc\"\n";

    static readonly Dictionary<string, int> layers = new();
    static readonly List<string> names = new();
    static readonly List<float> metres = new();
    static Texture2DArray? array;
    static bool ready;

    /// <summary>The layers' names, in layer order.</summary>
    public static IReadOnlyList<string> Names { get { Ensure(); return names; } }

    /// <summary>The texture array (null where there is no renderer to make one: a headless run).</summary>
    public static Texture2DArray? Array { get { Ensure(); return array; } }

    /// <summary>Loads the set and declares the global, once a process. Before any shader that includes the detail is made.</summary>
    public static void Ensure()
    {
        if (ready) return;
        ready = true;
        var index = Json.ParseString(FileAccess.GetFileAsString(DIR + "layers.json")).AsGodotDictionary();
        var images = new Godot.Collections.Array<Image>();
        foreach (var entry in index["layers"].AsGodotArray())
        {
            var layer = entry.AsGodotDictionary();
            string name = layer["name"].AsString();
            layers[name] = names.Count;
            names.Add(name);
            metres.Add((float)layer["metres"].AsDouble());
            var image = GD.Load<Texture2D>(DIR + name + ".png")?.GetImage();
            if (image == null) continue;
            if (image.IsCompressed()) image.Decompress();
            image.Convert(Image.Format.Rgb8);
            image.GenerateMipmaps();
            images.Add(image);
        }
        if (images.Count == names.Count && names.Count > 0)
        {
            array = new Texture2DArray();
            array.CreateFromImages(images);
        }
        else GD.PushWarning($"Surfaces: {images.Count} of {names.Count} detail textures loaded: the world is drawn without them");
        RenderingServer.GlobalShaderParameterAdd("surface_layers", RenderingServer.GlobalShaderParameterType.Sampler2Darray, array != null ? array.GetRid() : new Rid());
    }

    /// <summary>A shader from code that includes the detail (the global declared first).</summary>
    public static Shader Shader(string code)
    {
        Ensure();
        return new Shader { Code = code };
    }

    /// <summary>A layer's number in the array.</summary>
    public static int Layer(string name)
    {
        Ensure();
        return layers.TryGetValue(name, out int l) ? l : throw new ArgumentException($"no surface layer '{name}' (godot/assets/surfaces/layers.json)");
    }

    /// <summary>For a vertex's UV: this layer, laid over the face by where it is, at the layer's own size times `scale`.</summary>
    public static Vector2 What(string name, float scale = 1) => new(Layer(name) + 1, scale / metres[Layer(name)]);

    /// <summary>As What, turned a quarter: boards that stand upright, courses that run up a wall.</summary>
    public static Vector2 Turned(string name, float scale = 1) => new(Layer(name) + 1, -scale / metres[Layer(name)]);

    /// <summary>As What, but laid by the mesh's own coordinates: its UV2, in metres.</summary>
    public static Vector2 Mapped(string name, float scale = 1) => new(-(Layer(name) + 1), scale / metres[Layer(name)]);

    /// <summary>A UV for a face with no detail.</summary>
    public static readonly Vector2 None = Vector2.Zero;
}
