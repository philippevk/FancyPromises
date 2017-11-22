using System.Collections.Generic;

namespace FancyPromises
{
  public class ManualAsyncEngine : IAsyncEngine
  {
    private readonly List<Action> tickActions = new List<Action>();

    public void OnNextTick(Action action) {
      tickActions.Add(action); }

    public void Tick()
    {
      var n = tickActions.Count;
      Console.WriteLine("Tick: "+n);
      if (n==0) return;
      for (var i = 0; i<n; i++)
        tickActions[i]();
      tickActions.RemoveRange(0, n);
    }
  }
}
