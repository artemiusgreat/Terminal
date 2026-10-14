using Canvas.Core.Models;
using Canvas.Core.Shapes;
using Core.Enums;
using Core.Extensions;
using Core.Models;
using Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Dashboard.Components
{
  public partial class ChartsComponent : IAsyncDisposable, IDisposable
  {
    public class ChartComposer
    {
      public virtual IList<IShape> Items { get; set; } = [];
      public virtual Func<double, string> ShowIndex { get; set; }
    }

    protected class ChartPayload
    {
      public bool IsOrdinal { get; set; }
      public double? RangeFrom { get; set; }
      public double? RangeTo { get; set; }
      public Dictionary<string, string> Labels { get; set; } = [];
      public List<SeriesPayload> Series { get; set; } = [];
    }

    protected class SeriesPayload
    {
      public string Key { get; set; }
      public string Title { get; set; }
      public string Type { get; set; }
      public string Color { get; set; }
      public string LineColor { get; set; }
      public string TopColor { get; set; }
      public string BottomColor { get; set; }
      public string UpColor { get; set; }
      public string DownColor { get; set; }
      public string BorderUpColor { get; set; }
      public string BorderDownColor { get; set; }
      public string WickUpColor { get; set; }
      public string WickDownColor { get; set; }
      public List<object> Data { get; set; } = [];
    }

    [Inject] public virtual StateService Observer { get; set; }
    [Inject] public virtual IJSRuntime RuntimeService { get; set; }

    [Parameter] public virtual string Name { get; set; }

    protected virtual Section UpSide { get; set; }
    protected virtual Section DownSide { get; set; }
    protected virtual IDictionary<long, IShape> Indices { get; set; } = new ConcurrentDictionary<long, IShape>();
    protected virtual ElementReference Container { get; set; }
    protected virtual IJSObjectReference Module { get; set; }
    protected virtual IJSObjectReference Chart { get; set; }
    public virtual IList<IShape> Shapes { get; protected set; } = [];
    public virtual IList<ChartComposer> Composers { get; set; } = [];
    protected object sync = new();

    protected override async Task OnAfterRenderAsync(bool setup)
    {
      await base.OnAfterRenderAsync(setup);

      if (setup)
      {
        await EnsureChartAsync();

        Observer.Update += async state =>
        {
          if (state.Previous is SubscriptionEnum.Progress && state.Next is SubscriptionEnum.None)
          {
            await Clear();
          }
        };
      }
    }

    public virtual async Task Create(params string[] areas)
    {
      UpSide = new Section { Color = SKColors.DeepSkyBlue };
      DownSide = new Section { Color = SKColors.OrangeRed };
      Composers = [new() { Items = Shapes }];

      await EnsureChartAsync();
      await RenderAsync([], false, new() { IndexDomain = [0, 0] });
    }

    public virtual IShape GetShape<T>(Price price, SKColor? color = null) where T : IShape, new()
    {
      if (typeof(T).Equals(typeof(CandleShape)))
      {
        return new CandleShape
        {
          L = price.Bar.Low,
          H = price.Bar.High,
          O = price.Bar.Open,
          C = price.Bar.Close,
          Component = price.Bar.Close > price.Bar.Open ? UpSide : DownSide
        };
      }

      return new T
      {
        Y = price.Last,
        Component = new() { Color = color ?? SKColors.LimeGreen }
      };
    }

    public virtual void Update(long index, string area, string series, params IShape[] inputs)
    {
      ChartPayload payload;
      Dimension domain;

      lock (sync)
      {
        if (Indices.TryGetValue(index, out var currentPoint) is false)
        {
          currentPoint = new Shape { X = index };
          Shapes.Add(currentPoint);
          Indices[index] = currentPoint;
        }

        foreach (var input in inputs)
        {
          currentPoint.Groups[area] = currentPoint.Groups.Get(area) ?? new Shape();
          currentPoint.Groups[area].Groups[series] = input;
        }

        domain = new Dimension
        {
          IndexDomain = [Shapes.Count - Math.Max(10, Shapes.Count) - 1, Shapes.Count]
        };

        payload = BuildPayload(Shapes, false, domain);
      }

      if (Observer.State.Next is SubscriptionEnum.None)
      {
        return;
      }

      _ = InvokeAsync(() => RenderPayloadAsync(payload));
    }

    public virtual void UpdateOrdinals(IList<IShape> shapes)
    {
      if (Observer.State.Next is SubscriptionEnum.None)
      {
        return;
      }

      var domain = new Dimension
      {
        IndexDomain = [0, shapes.Count]
      };

      _ = InvokeAsync(() => RenderPayloadAsync(BuildPayload(shapes, true, domain)));
    }

    public virtual async Task Clear()
    {
      Shapes.Clear();
      Indices.Clear();

      await EnsureChartAsync();

      if (Chart is not null)
      {
        await Module.InvokeVoidAsync("clearChart", Chart);
      }
    }

    public virtual void Dispose() => _ = DisposeAsync();

    public virtual async ValueTask DisposeAsync()
    {
      if (Module is not null && Chart is not null)
      {
        await Module.InvokeVoidAsync("disposeChart", Chart);
      }

      if (Chart is not null)
      {
        await Chart.DisposeAsync();
      }

      if (Module is not null)
      {
        await Module.DisposeAsync();
      }
    }

    protected virtual async Task EnsureChartAsync()
    {
      Module ??= await RuntimeService.InvokeAsync<IJSObjectReference>("import", "./js/lightweightChartsComponent.js");
      Chart ??= await Module.InvokeAsync<IJSObjectReference>("createChart", Container);
    }

    protected virtual async Task RenderPayloadAsync(ChartPayload payload)
    {
      await EnsureChartAsync();
      await Module.InvokeVoidAsync("setChartData", Chart, payload);
    }

    protected virtual Task RenderAsync(IList<IShape> shapes, bool isOrdinal, Dimension domain)
    {
      return RenderPayloadAsync(BuildPayload(shapes, isOrdinal, domain));
    }

    protected virtual ChartPayload BuildPayload(IList<IShape> shapes, bool isOrdinal, Dimension domain)
    {
      var payload = new ChartPayload
      {
        IsOrdinal = isOrdinal,
        RangeFrom = domain.IndexDomain?.FirstOrDefault(),
        RangeTo = domain.IndexDomain?.LastOrDefault()
      };
      var map = new Dictionary<string, SeriesPayload>();
      var composer = Composers.FirstOrDefault();

      for (var i = 0; i < shapes.Count; i++)
      {
        var point = shapes[i];
        var time = GetTime(point, i, isOrdinal);
        var label = GetLabel(point, i, isOrdinal, composer);

        if (string.IsNullOrWhiteSpace(label) is false)
        {
          payload.Labels[$"{time}"] = label;
        }

        foreach (var area in point?.Groups ?? new Dictionary<string, IShape>())
        {
          foreach (var series in area.Value?.Groups ?? new Dictionary<string, IShape>())
          {
            var key = $"{area.Key}:{series.Key}";

            if (TryCreateSeries(key, series.Key, series.Value, out var output) is false)
            {
              continue;
            }

            if (map.TryGetValue(key, out var definition) is false)
            {
              definition = output;
              map[key] = definition;
              payload.Series.Add(definition);
            }

            definition.Data.Add(CreatePoint(time, series.Value));
          }
        }
      }

      return payload;
    }

    protected virtual long GetTime(IShape shape, int index, bool isOrdinal)
    {
      return index + 1;
    }

    protected virtual string GetLabel(IShape shape, int index, bool isOrdinal, ChartComposer composer)
    {
      if (composer?.ShowIndex is not null)
      {
        return composer.ShowIndex(index);
      }

      if (isOrdinal)
      {
        return $"{index + 1}";
      }

      var ticks = Convert.ToInt64(shape?.X ?? DateTime.UtcNow.Ticks);
      return $"{ticks.ToDateTime():MMM d HH:mm:ss}";
    }

    protected virtual bool TryCreateSeries(string key, string title, IShape shape, out SeriesPayload series)
    {
      series = default;

      if (shape is CandleShape candle)
      {
        var candleColor = TryGetColor(candle.Component, UpSide.Color);
        var downColor = ToCssColor(DownSide.Color);
        var upColor = ToCssColor(candleColor);

        series = new SeriesPayload
        {
          Key = key,
          Title = title,
          Type = "candlestick",
          UpColor = upColor,
          DownColor = downColor,
          BorderUpColor = upColor,
          BorderDownColor = downColor,
          WickUpColor = upColor,
          WickDownColor = downColor
        };

        return true;
      }

      if (shape is AreaShape area)
      {
        var areaColor = TryGetColor(area.Component, SKColors.LimeGreen);

        series = new SeriesPayload
        {
          Key = key,
          Title = title,
          Type = "area",
          LineColor = ToCssColor(areaColor),
          TopColor = ToCssColor(areaColor, 0.35f),
          BottomColor = ToCssColor(areaColor, 0.05f)
        };

        return true;
      }

      var lineColor = TryGetColor(shape?.Component, SKColors.LimeGreen);

      series = new SeriesPayload
      {
        Key = key,
        Title = title,
        Type = "line",
        Color = ToCssColor(lineColor)
      };

      return true;
    }

    protected virtual object CreatePoint(long time, IShape shape)
    {
      if (shape is CandleShape candle)
      {
        return new { Time = time, Open = candle.O ?? 0, High = candle.H ?? 0, Low = candle.L ?? 0, Close = candle.C ?? 0 };
      }

      return new { Time = time, Value = shape?.Y ?? 0 };
    }

    protected virtual SKColor TryGetColor(object component, SKColor fallback)
    {
      return component is Section section ? section.Color : fallback;
    }

    protected virtual string ToCssColor(SKColor color, float opacity = 1)
    {
      var alpha = Math.Clamp(color.Alpha / 255f * opacity, 0, 1);
      return $"rgba({color.Red}, {color.Green}, {color.Blue}, {alpha:0.###})";
    }
  }
}
