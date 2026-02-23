using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nodus.Shared.Models;

public class SyncStats
{
    public int PendingVotes { get; set; }
    public int PendingMedia { get; set; }
    public int SyncedVotes { get; set; }
    public int TotalVotes { get; set; }
    public double SyncPercentage => TotalVotes == 0 ? 0 : (double)SyncedVotes / TotalVotes * 100;
}
