using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisualVersionofService.Comunications
{
    internal class Disposable
    {
        public string Name = "";
        private IDisposable IDisposable;

        public Disposable(string name, IDisposable idisposable)
        {
            Name = name;
            IDisposable = idisposable;
        }

        public void Dispose()
        {
            IDisposable.Dispose();
        }
    }
}