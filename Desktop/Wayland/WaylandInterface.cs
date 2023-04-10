using WaylandSharp;
using WlxOverlay.Core;
using WlxOverlay.Desktop.Pipewire;
using WlxOverlay.Numerics;
using WlxOverlay.Overlays.Simple;
using WlxOverlay.Overlays.Wayland;
using WlxOverlay.Types;

namespace WlxOverlay.Desktop.Wayland;

public class WaylandInterface : IDisposable
{
    public static WaylandInterface? Instance;

    private readonly Dictionary<uint, WaylandOutput> _outputs = new();
    public Rect2 OutputRect { get; private set; }

    private readonly List<Type> _supportedScreenTypes = new();

    private readonly WlDisplay _display;
    private ZxdgOutputManagerV1? _outputManager;
    private WlSeat? _seat;

    public static void Initialize()
    {
        if (Instance != null)
            throw new ApplicationException($"Can't have more than one {nameof(WaylandInterface)}!");

        Instance = new WaylandInterface();
        Instance.RoundTrip();
    }

    public void RoundTrip()
    {
        _display.Roundtrip();
    }

    public IAsyncEnumerable<BaseOverlay> CreateScreensAsync()
    {
        IAsyncEnumerable<BaseOverlay> UseWlrDmaBuf()
        {
            Console.WriteLine($"Using desktop capture protocol: {WlInterface.ZwlrExportDmabufManagerV1.Name}");
            return _outputs.Values.Select(x => new WlrDmaBufScreen(x)).AsAsync();
        }

        IAsyncEnumerable<BaseOverlay> UseWlrScreenCopy()
        {
            Console.WriteLine($"Using desktop capture protocol: {WlInterface.ZwlrScreencopyManagerV1.Name}");
            return _outputs.Values.Select(x => new WlrScreenCopyScreen(x)).AsAsync();
        }

        IAsyncEnumerable<BaseOverlay> UseKdeScreenCast()
        {
            Console.WriteLine($"Using desktop capture protocol: {WlInterface.ZkdeScreencastUnstableV1.Name}");
            return _outputs.Values.Select(x => new KdeScreenCastScreen(x)).AsAsync();
        }

        async IAsyncEnumerable<BaseOverlay> UsePipeWire(bool dmaBuf)
        {
            Console.WriteLine("Using PipeWire capture.");
            PipeWireCapture.Load(dmaBuf);

            if (_outputs.Values.Count > 0)
            {
                Console.WriteLine(" You will be prompted one screen at a time.\n" +
                                  " Please select the corresponding screen on the prompt.\n" +
                                  " Cancel the prompt if you do not wish to capture the given screen. \n" +
                                  " If your compositor supports org.freedesktop.portal.ScreenCast v4, you will only be prompted once.");

                foreach (var output in _outputs.Values)
                {
                    Console.WriteLine(" --- Prompting for screen: " + output.Name + " ---");
                    var data = await XdgScreenCastHandler.PromptUserAsync(output);
                    if (data != null)
                    {
                        var screen = new PipeWireScreen(data);
                        Console.WriteLine($"{data.Name} -> {data.NodeId}");
                        yield return screen;
                    }
                    else
                        Console.WriteLine($"{output.Name} will not be used.");
                }
            }
            else
            {
                Console.WriteLine(" ERROR Could not poll Wayland outputs.\n" +
                                  " You may still use WlxOverlay in single-screen mode.\n" +
                                  " Select your screen which is positioned at 0,0.");

                var output = new WaylandOutput(0, null) { Name = "Default" };
                var data = await XdgScreenCastHandler.PromptUserAsync(output);
                if (data != null)
                {
                    var screen = new PipeWireScreenSingle(data);
                    Console.WriteLine($"{data.Name} -> {data.NodeId}");
                    yield return screen;
                }
                else
                    Console.WriteLine($"{output.Name} will not be used.");
            }
        }

        switch (Config.Instance.WaylandCapture)
        {
            case "dmabuf":
                return UseWlrDmaBuf();
            case "screencopy":
                return UseWlrScreenCopy();
            case "kde":
                return UseKdeScreenCast();
            case "pipewire":
                return UsePipeWire(dmaBuf: true);
            case "pw-fallback":
                return UsePipeWire(dmaBuf: false);
            default:
                if (_supportedScreenTypes.Contains(typeof(WlrDmaBufScreen)))
                    return UseWlrDmaBuf();
                if (_supportedScreenTypes.Contains(typeof(WlrScreenCopyScreen)))
                    return UseWlrScreenCopy();
                if (_supportedScreenTypes.Contains(typeof(KdeScreenCastScreen)))
                    return UseKdeScreenCast();

                return UsePipeWire(dmaBuf: true);
        }
    }

    private WaylandInterface()
    {
        _display = WlDisplay.Connect();

        var reg = _display.GetRegistry();

        reg.Global += (_, e) =>
        {
            if (e.Interface == WlInterface.WlOutput.Name)
#pragma warning disable CS4014
#pragma warning disable VSTHRD110
                CreateOutputAsync(reg, e);
            else if (e.Interface == WlInterface.WlSeat.Name)
                _seat = reg.Bind<WlSeat>(e.Name, e.Interface, e.Version);
            else if (e.Interface == WlInterface.ZxdgOutputManagerV1.Name)
                _outputManager = reg.Bind<ZxdgOutputManagerV1>(e.Name, e.Interface, e.Version);
            else if (e.Interface == WlInterface.ZwlrExportDmabufManagerV1.Name)
                _supportedScreenTypes.Add(typeof(WlrDmaBufScreen));
            else if (e.Interface == WlInterface.ZwlrScreencopyManagerV1.Name)
                _supportedScreenTypes.Add(typeof(WlrScreenCopyScreen));
            else if (e.Interface == WlInterface.ZkdeScreencastUnstableV1.Name)
                _supportedScreenTypes.Add(typeof(KdeScreenCastScreen));
        };

        reg.GlobalRemove += (_, e) =>
        {
            if (!_outputs.TryGetValue(e.Name, out var output))
                return;

            output.Dispose();
            _outputs.Remove(e.Name);
        };
    }

    private async Task CreateOutputAsync(WlRegistry reg, WlRegistry.GlobalEventArgs e)
    {
        var wlOutput = reg.Bind<WlOutput>(e.Name, e.Interface, e.Version);

        while (_outputManager == null)
            await Task.Delay(10);

        var obj = new WaylandOutput(e.Name, wlOutput);
        
        wlOutput.Geometry += obj.SetGeometry;
        wlOutput.Mode += obj.SetMode;

        using var xdgOutput = _outputManager.GetXdgOutput(wlOutput);
        xdgOutput.Name += obj.SetName;
        xdgOutput.LogicalSize += obj.SetSize;
        xdgOutput.LogicalPosition += obj.SetPosition;
        _display.Roundtrip();

        _outputs.Add(e.Name, obj);
        obj.RecalculateTransform();
        RecalculateOutputRect();
    }

    private void RecalculateOutputRect()
    {
        OutputRect = new Rect2();
        foreach (var output in _outputs.Values)
        {
            var origin = output.Transform * Vector2.Zero;
            var size = output.Transform * Vector2.One - origin;
            if (size.x < 0)
            {
                origin.x += size.x;
                size.x = -size.x;
            }
            if (size.y < 0)
            {
                origin.y += size.y;
                size.y = -size.y;
            }
            
            var rect = new Rect2(origin, size);
            OutputRect = OutputRect.Merge(rect);
        }
    }

    public void Dispose()
    {
        Thread.Sleep(5);

        foreach (var output in _outputs.Values)
            output.Dispose();

        _seat?.Dispose();
        _outputManager?.Dispose();
        _display.Dispose();
    }
}
