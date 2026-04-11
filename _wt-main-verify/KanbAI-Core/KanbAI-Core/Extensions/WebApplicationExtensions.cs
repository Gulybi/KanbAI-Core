using System.Runtime.CompilerServices;
using Scalar.AspNetCore;

namespace KanbAI_Core.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Registers the Scalar API reference UI in a separate method so the
    /// JIT compiler only loads <c>Scalar.AspNetCore</c> when this method is
    /// actually invoked. This prevents <see cref="System.IO.FileLoadException"/>
    /// on machines where WDAC blocks the unsigned Scalar DLL.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static WebApplication MapScalarUi(this WebApplication app)
    {
        app.MapScalarApiReference();
        return app;
    }
}
