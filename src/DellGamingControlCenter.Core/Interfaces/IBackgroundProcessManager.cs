namespace DellGamingControlCenter.Core.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;

public interface IBackgroundProcessManager
{
    /// <summary>Analyzes running processes and suggests which ones can be closed safely based on rules.</summary>
    Task<List<string>> GetClosableProcessesAsync();

    /// <summary>Requests a graceful close of the specified processes.</summary>
    Task<bool> RequestCloseAsync(IEnumerable<string> processNames);
}
