using System.Drawing;

namespace Karpik.Engine.Shared.Network.Core;

public interface IWriter
{
    public void Put(float value);
    public void Put(int value);
    public void Put(long value);
    public void Put(bool value);
    public void Put(string value);
    public void Put(byte value);
    public void Put(ushort value);
    public void Put(double value);
    public void Put(Color color);
    
    public void Reset();
}
