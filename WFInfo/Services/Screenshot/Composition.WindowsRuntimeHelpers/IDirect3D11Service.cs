using Windows.Graphics.DirectX.Direct3D11;

namespace WFInfo.Services.Screenshot.Composition.WindowsRuntimeHelpers;

public interface IDirect3D11Service
{
    IDirect3DDevice? CreateDirect3DDeviceFromSharpDxDevice(SharpDX.Direct3D11.Device sharpDxDevice);
    SharpDX.Direct3D11.Texture2D CreateSharpDXTexture2D(IDirect3DSurface surface);
}
