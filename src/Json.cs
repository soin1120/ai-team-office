using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AITeam
{
    // JSON 읽기/쓰기 도우미 (.NET Framework 기본 JavaScriptSerializer 사용)
    static class Json
    {
        static JavaScriptSerializer Make()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = int.MaxValue;
            s.RecursionLimit = 256;
            return s;
        }

        public static object Parse(string text)
        {
            try { return Make().DeserializeObject(text); } catch { return null; }
        }

        public static string Stringify(object o) { return Make().Serialize(o); }

        // 경로로 값 꺼내기: Json.Get(obj, "item", "text")
        public static object Get(object o, params string[] path)
        {
            foreach (var key in path)
            {
                var d = o as IDictionary<string, object>;
                if (d == null || !d.TryGetValue(key, out o)) return null;
            }
            return o;
        }

        public static string Str(object o, params string[] path)
        {
            var v = Get(o, path);
            return v == null ? null : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static List<object> Arr(object o, params string[] path)
        {
            var v = Get(o, path);
            var list = new List<object>();
            var e = v as IEnumerable;
            if (e == null || v is string) return list;
            foreach (var x in e) list.Add(x);
            return list;
        }

        public static long Num(object o, params string[] path)
        {
            var v = Get(o, path);
            if (v == null) return 0;
            try { return Convert.ToInt64(v); } catch { return 0; }
        }

        // 에이전트가 쓴 글에서 JSON 객체 하나를 찾아낸다 (```json 블록 우선)
        public static object Extract(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int fence = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
            if (fence >= 0)
            {
                int start = text.IndexOf('\n', fence);
                int end = start < 0 ? -1 : text.IndexOf("```", start);
                if (start >= 0 && end > start)
                {
                    var o = Parse(text.Substring(start, end - start));
                    if (o is IDictionary<string, object>) return o;
                }
            }
            int a = text.IndexOf('{'), b = text.LastIndexOf('}');
            while (a >= 0 && b > a)
            {
                var o = Parse(text.Substring(a, b - a + 1));
                if (o is IDictionary<string, object>) return o;
                a = text.IndexOf('{', a + 1);
            }
            return null;
        }
    }
}
