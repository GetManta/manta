using System;

namespace Manta.Sceleton.Converters
{
    public interface IUpConverterFactory
    {
        IUpConvertMessage CreateInstanceFor(Type messageType);
        object Execute(IUpConvertMessage converter, Type messageType, object message);
    }
}