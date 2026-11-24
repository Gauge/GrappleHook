Measured 2026-11-23 17:12, .NET 9.0.21, 15 samples, median ns per op.

| Scenario | Baseline ns | Current ns | Speedup | Current B/op |
| --- | ---: | ---: | ---: | ---: |
| client/single | 2,874 | 2,866 | 1.00x | 0 |
| client/taut | 127 | 125 | 1.02x | 0 |
| client/zero-g-tow | 431 | 155 | 2.78x | 0 |
| client/wrapped-8 | 2,002 | 1,999 | 1.00x | 0 |
| client/100-points | 15,018 | 14,976 | 1.00x | 0 |
| client/restart | 775 | 439 | 1.76x | 0 |
| dedicated/single | 719 | 381 | 1.89x | 0 |
| dedicated/wrapped-8 | 1,415 | 1,211 | 1.17x | 0 |
| interact/ray-20 | 319 | 92 | 3.46x | 0 |
| interact/ray-100 | 1,633 | 412 | 3.96x | 0 |
| interact/ray-sweep | 287 | 139 | 2.07x | 0 |
| zipline/4-riders | 87 | 87 | 1.00x | 0 |
| zipline/clip-on | 325 | 325 | 1.00x | 0 |

* `client/single`: One frame of a slack rope in 1 g air: 40 m, 10% slack, 20 points (the default), launcher bobbing.
* `client/taut`: One frame of a rope pulled tight: drawn straight.
* `client/zero-g-tow`: One frame of a slack rope in space, both ends travelling at 100 m/s.
* `client/wrapped-8`: One frame of a rope bent over 8 edges (Rope.Wrapping.MaxWraps): 9 spans sharing 20 points.
* `client/100-points`: As client/single at Rope.DrawPoints = 100, the maximum.
* `client/restart`: A chain started from its hanging shape (the rope coming into view, or its ends jumping): catenary solve plus reset.
* `dedicated/single`: One frame of the exact catenary a dedicated server builds while someone rides: 20 points.
* `dedicated/wrapped-8`: The dedicated server's catenary over 8 wrap points: 9 spans.
* `interact/ray-20`: The per-frame check whether the player is looking at the rope (zipline grab), 20 points, a miss.
* `interact/ray-100`: As interact/ray-20 on 100 points.
* `interact/ray-sweep`: The player standing 1-5 m from the rope sweeping their view across it: hits and misses mixed, 20 points.
* `zipline/4-riders`: Each rider's pulley found on the rope every frame: 4 riders on 20 points.
* `zipline/clip-on`: Finding where a rider clips on (closest point on the rope), 100 points.
