using WlxOverlay.GFX;

namespace WlxOverlay.GUI;

/// <summary>
/// A rectangular image
/// </summary>
public class Image : Control
{
    private readonly ITexture _texture;
    public Image(int x, int y, uint w, uint h, ITexture texture) : base(x, y, w, h)
    {
        _texture = texture;
    }

    public override void Render()
    {
        GraphicsEngine.Renderer.DrawSprite(_texture, X, Y, Width, Height);
    }
}