using Silk.NET.OpenGL;

namespace WlxOverlay.GFX.OpenGL;
public class GlShader : IDisposable
{
    private uint _handle;
    private GL _gl;

    public GlShader(GL gl, string vertexPath, string fragmentPath)
    {
        _gl = gl;

        var vertex = LoadShader(ShaderType.VertexShader, vertexPath);
        var fragment = LoadShader(ShaderType.FragmentShader, fragmentPath);
        _handle = _gl.CreateProgram();
        _gl.DebugAssertSuccess();
        
        _gl.AttachShader(_handle, vertex);
        _gl.AttachShader(_handle, fragment);
        _gl.LinkProgram(_handle);
        _gl.DebugAssertSuccess();
        
        _gl.GetProgram(_handle, GLEnum.LinkStatus, out var status);
        if (status == 0)
        {
            throw new Exception($"Program failed to link with error: {_gl.GetProgramInfoLog(_handle)}");
        }
        _gl.DetachShader(_handle, vertex);
        _gl.DetachShader(_handle, fragment);
        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);
        _gl.DebugAssertSuccess();
    }

    public void Use()
    {
        _gl.UseProgram(_handle);
        _gl.DebugAssertSuccess();
    }

    public void SetUniform(string name, int value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
        _gl.DebugAssertSuccess();
    }

    public void SetUniform(string name, float value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform1(location, value);
        _gl.DebugAssertSuccess();
    }

    public void SetUniformM4(string name, float[] value)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }

        _gl.UniformMatrix4(location, 1, false, value);
        _gl.DebugAssertSuccess();
    }

    public void SetUniform(string name, float x, float y, float z, float w)
    {
        int location = _gl.GetUniformLocation(_handle, name);
        if (location == -1)
        {
            throw new Exception($"{name} uniform not found on shader.");
        }
        _gl.Uniform4(location, x, y, z, w);
        _gl.DebugAssertSuccess();
    }

    public void Dispose()
    {
        _gl.DeleteProgram(_handle);
        _gl.DebugAssertSuccess();
    }

    private uint LoadShader(ShaderType type, string path)
    {
        var src = File.ReadAllText(path);
        var handle = _gl.CreateShader(type);
        _gl.DebugAssertSuccess();
        
        _gl.ShaderSource(handle, src);
        _gl.DebugAssertSuccess();
        
        _gl.CompileShader(handle);
        _gl.DebugAssertSuccess();
        
        var infoLog = _gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
        }
        _gl.DebugAssertSuccess();

        return handle;
    }
}