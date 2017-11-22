using System;
using System.Collections.Generic;

/**
 * TODO:
 * - IPromise
 * - Then(Func<T, object> cb)
 * - Cast<T>();
 * - Aggregate functions (Parallel, Sequence, etc)
 */

namespace FancyPromises
{
  public class Promise<T>
  {
    private bool isSettled;
    private T value;
    private Exception reason;
    private Action settleAction;
    private List<Action> extraSettleActions;

    public Promise(Action<Promise<T>> action)
    {
      if (action==null)
        return;
      try { action(this); }
      catch(Exception ex) { Settle(ex, default(T)); }
    }

    public Promise(T valueArg)
    {
      isSettled = true;
      value = valueArg;
    }

    public Promise(Exception reasonArg)
    {
      if (reasonArg==null)
        throw new Exception("Cannot reject: reason cannot be null");
      isSettled = true;
      this.reason = reasonArg;
    }

    public Promise() { }

    // -- Settlement ------

    public Promise<T> Resolve(T valueArg)
    {
      return Settle(null, valueArg);
    }

    public Promise<T> Reject(Exception reasonArg)
    {
      if (reasonArg==null)
        throw new Exception("Cannot reject: reason cannot be null");
      return Settle(reasonArg, default(T));
    }

    private Promise<T> Settle(Exception reasonArg, T valueArg)
    {
      if (isSettled)
        throw new Exception("Cannot settle: already settled");

      isSettled = true;
      reason = reasonArg;
      value = valueArg;

      if (settleAction!=null)
      {
        PromiseConfig.AsyncEngine.OnNextTick(settleAction);
        settleAction = null;
        if (extraSettleActions!=null)
        {
          for (var i=0; i<extraSettleActions.Count; i++)
            PromiseConfig.AsyncEngine.OnNextTick(extraSettleActions[i]);
          extraSettleActions = null;
        }
      }

      return this;
    }

    private Promise<TOut> _OnSettle<TOut>(Func<Promise<TOut>, Promise<TOut>> cb)
    {
      var prom = new Promise<TOut>();

      Action tickAction = ()=>
      {
        try
        {
          var cbProm = cb(prom);

          if (prom.isSettled)
          {
            if (cbProm!=null&&cbProm!=prom)
              throw new Exception("woops, there's a bug in Promise code");
            return;
          }

          if (cbProm==null)
          {
            // XXX philippe: is this really the correct behavior?
            if (default(TOut)!=null)
              throw new Exception("Cannot cast null to "+typeof(TOut));
            prom.Settle(cbProm.reason, default(TOut));
            return;
          }

          if (cbProm.isSettled)
            prom.Settle(cbProm.reason, cbProm.value);
          else
          {
            Action sact = ()=>prom.Settle(cbProm.reason, cbProm.value);
            if (cbProm.settleAction==null)
              cbProm.settleAction = sact;
            else
            {
              if (cbProm.extraSettleActions==null)
                cbProm.extraSettleActions = new List<Action>();
              cbProm.extraSettleActions.Add(sact);
            }
          }
        }
        catch (Exception ex) { prom.Settle(ex, default(TOut)); }
      };

      if (isSettled)
        PromiseConfig.AsyncEngine.OnNextTick(tickAction);
      else if (settleAction==null)
        settleAction = tickAction;
      else
      {
        if (extraSettleActions==null)
          extraSettleActions = new List<Action>();
        extraSettleActions.Add(settleAction);
      }

      return prom;
    }

    // -- Then, dual cbs ------
    public Promise<TOut> Then<TOut>(Func<T, Promise<TOut>> cb,
      Func<Exception, Promise<TOut>> errCb)
    {
      return _OnSettle<TOut>(prom=>
      {
        if (reason!=null)
          return errCb!=null ? errCb(reason) : null;
        return cb!=null ? cb(value) : null;
      });
    }

    // -- Then ------
    public Promise<TOut> Then<TOut>(Func<T, Promise<TOut>> cb)
    {
      return _OnSettle<TOut>(prom=>
      {
        if (reason!=null)
          return prom.Settle(reason, default(TOut));
        return cb!=null ? cb(value) : null;
      });
    }
    public Promise<T> Then(Action<T> cb)
    {
      return _OnSettle<T>(prom=>
      {
        if (reason!=null)
          return prom.Settle(reason, default(T));
        if (cb!=null)
          cb(value);
        return null;
      });
    }
    public Promise<TOut> ThenSync<TOut>(Func<T, TOut> cb)
    {
      return _OnSettle<TOut>(prom=>
      {
        if (reason!=null)
          return prom.Settle(reason, default(TOut));
        return cb!=null ? prom.Settle(null, cb(value)) : null;
      });
    }

    // -- Generic Catch --
    public Promise<T> Catch(Func<Exception, Promise<T>> cb)
    {
      return _OnSettle<T>(prom=>
      {
        if (reason==null)
          return prom.Settle(null, value);
        return cb!=null ? cb(reason) : null;
      });
    }
    public Promise<T> Catch(Action<Exception> cb)
    {
      return _OnSettle<T>(prom=>
      {
        if (reason==null)
          return prom.Settle(null, value);
        if (cb!=null)
          cb(reason);
        return null;
      });
    }
    public Promise<T> CatchSync(Func<Exception, T> cb)
    {
      return _OnSettle<T>(prom=>
      {
        if (reason==null)
          return prom.Settle(null, value);
        return cb!=null ? prom.Settle(null, cb(reason)) : null;
      });
    }

    // -- Typed Catch --
    public Promise<T> Catch<TEx>(Func<TEx, Promise<T>> cb) where TEx : Exception
    {
      return _OnSettle<T>(prom=>
      {
        var castedEx = reason as TEx;
        if (castedEx==null)
          return prom.Settle(reason, value);
        return cb!=null ? cb(castedEx) : null;
      });
    }
    public Promise<T> Catch<TEx>(Action<TEx> cb) where TEx : Exception
    {
      return _OnSettle<T>(prom=>
      {
        var castedEx = reason as TEx;
        if (castedEx==null)
          return prom.Settle(reason, value);
        if (cb!=null)
          cb(castedEx);
        return null;
      });
    }
    public Promise<T> CatchSync<TEx>(Func<TEx, T> cb) where TEx : Exception
    {
      return _OnSettle<T>(prom=>
      {
        var castedEx = reason as TEx;
        if (castedEx==null)
          return prom.Settle(reason, value);
        return cb!=null ? prom.Settle(null, cb(castedEx)) : null;
      });
    }

    // -- Other settlement handlers --
    public Promise<T> Finally(Action<Exception, T> cb)
    {
      return _OnSettle<T>(prom=>
      {
        cb(reason, value);
        return prom.Settle(reason, value);
      });
    }

    // -- State ------
    public bool IsSettled() { return isSettled; }
    public bool IsResolved() { return isSettled&&reason==null; }
    public bool IsRejected() { return isSettled&&reason!=null; }
  }

  public interface IAsyncEngine
  {
    void OnNextTick(Action action);
  }

  public static class PromiseConfig
  {
    public static IAsyncEngine AsyncEngine;
  }
}
