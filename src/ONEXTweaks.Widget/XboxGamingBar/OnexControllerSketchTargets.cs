// SPDX-License-Identifier: AGPL-3.0-only
using System.Collections.Generic;
using Windows.Foundation;

namespace XboxGamingBar
{
    public sealed partial class GamingWidget
    {
        // Touch regions in the 1280 × 1012 artwork coordinates.
        private static readonly Dictionary<string, ControllerSketchTarget> ControllerSketchTargets =
            new Dictionary<string, ControllerSketchTarget>
            {
                ["LT"] = new ControllerSketchTarget(new Point(146, 78), "41,38 258,38 258,114 175,120 144,110 104,104 70,101 41,102"),
                ["LB"] = new ControllerSketchTarget(new Point(109, 132), "41,102 70,101 104,104 144,110 175,120 184,128 155,175 42,177"),
                ["RT"] = new ControllerSketchTarget(new Point(1059, 78), "1164,38 947,38 947,114 1030,120 1061,110 1101,104 1135,101 1164,102"),
                ["RB"] = new ControllerSketchTarget(new Point(1096, 132), "1164,102 1135,101 1101,104 1061,110 1030,120 1021,128 1050,175 1163,177"),
                ["OEM:M1"] = new ControllerSketchTarget(new Point(269, 430), "206,379 332,379 332,481 243.29,481 206,464.15"),
                ["OEM:M2"] = new ControllerSketchTarget(new Point(1086, 430), "1023,379 1149,379 1149,481 1032.51,481 1023,471.4"),
                ["Select"] = new ControllerSketchTarget(new Point(222, 534), "184,499 260,499 260,569 187.7,569 184,567.46"),
                ["Start"] = new ControllerSketchTarget(new Point(981, 534), "943,499 1019,499 1019,562.63 1015.15,569 943,569"),
                ["LSClick"] = new ControllerSketchTarget(new Point(130, 618), "101.5,646 101.5,590 158.5,590 158.5,646"),
                ["LSUp"] = new ControllerSketchTarget(new Point(130, 562), "97,531.5 163,531.5 163,585.42 158.5,590 101.5,590 97,585.42"),
                ["LSLeft"] = new ControllerSketchTarget(new Point(73, 618), "42.5,585.5 97.08,585.5 101.5,590 101.5,646 97.08,650.5 42.5,650.5"),
                ["LSRight"] = new ControllerSketchTarget(new Point(187, 618), "162.92,585.5 217.5,585.5 217.5,650.5 162.92,650.5 158.5,646 158.5,590"),
                ["LSDown"] = new ControllerSketchTarget(new Point(130, 674), "162.5,650.07 162.5,671.67 151.96,695.96 133.43,704 97.5,704 97.5,650.07 101.5,646 158.5,646"),
                ["DPadUp"] = new ControllerSketchTarget(new Point(183, 697), "163.88,668.5 212,668.5 212,698 184.5,725.5 181.5,725.5 154,698 154,691.26"),
                ["DPadLeft"] = new ControllerSketchTarget(new Point(153, 727), "147.26,698 154,698 181.5,725.5 181.5,728.5 154,756 124.5,756 124.5,707.88"),
                ["DPadRight"] = new ControllerSketchTarget(new Point(213, 727), "212,698 241.5,698 241.5,756 212,756 184.5,728.5 184.5,725.5"),
                ["DPadDown"] = new ControllerSketchTarget(new Point(183, 757), "181.5,728.5 184.5,728.5 212,756 212,785.5 154,785.5 154,756"),
                ["OEM:Home"] = new ControllerSketchTarget(new Point(216, 830), "184.02,800.5 254,800.5 254,850.5 178,850.5 178,803.22"),
                ["OEM:GKey"] = new ControllerSketchTarget(new Point(216, 871), "254,850.5 254,900.5 178,900.5 178,850.5"),
                ["Y"] = new ControllerSketchTarget(new Point(1062, 583), "1027.85,548 1097,548 1097,583.01 1062.99,618 1062,618 1027,583 1027,549.41"),
                ["X"] = new ControllerSketchTarget(new Point(1027, 618), "992,583 1027,583 1062,618 1062,618 1034.57,645.43 992,635.88"),
                ["B"] = new ControllerSketchTarget(new Point(1098, 618), "1097.01,583 1133,583 1133,653 1097.01,653 1063,618.01 1063,617.99"),
                ["A"] = new ControllerSketchTarget(new Point(1062, 653), "1062,618 1062.99,618 1097,652.99 1097,680.65 1061.82,688 1047.52,688 1034.57,645.43"),
                ["RSClick"] = new ControllerSketchTarget(new Point(1020, 720), "992,748.5 992,695.46 1048,691.24 1048,748.5"),
                ["RSUp"] = new ControllerSketchTarget(new Point(1016, 667), "986,637.5 999.23,637.5 1034.57,645.43 1046,683 1046,691.39 992,695.46 986,689.58"),
                ["RSLeft"] = new ControllerSketchTarget(new Point(964, 720), "933.5,687.5 983.88,687.5 992,695.46 992,748.5 987.93,752.5 933.5,752.5"),
                ["RSRight"] = new ControllerSketchTarget(new Point(1076, 720), "1064.21,687.5 1106.5,687.5 1106.5,752.5 1052.07,752.5 1048,748.5 1048,691.24 1048.38,690.81"),
                ["RSDown"] = new ControllerSketchTarget(new Point(1020, 777), "1052.5,752.92 1052.5,807 1009.8,807 987.5,793.54 987.5,752.92 992,748.5 1048,748.5"),
                ["OEM:KeyboardMouse"] = new ControllerSketchTarget(new Point(988, 830), "950,800 998.2,800 1026,816.78 1026,850.5 950,850.5"),
                ["OEM:Onex"] = new ControllerSketchTarget(new Point(988, 871), "1026,850.5 1026,901 950,901 950,850.5"),
            };
    }
}
