using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisualVersionofService.Comunications
{
    public class Disposable
    {
        public string Name;
        private IDisposable IDisposable;
        public Disposable(string name,IDisposable disposable)
        {
            Name = name;
            IDisposable = disposable;
        }
        public void Dispose()
        {
            IDisposable.Dispose();
        }
    }
}
