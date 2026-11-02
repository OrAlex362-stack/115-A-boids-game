using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BoidArena
{
    // 找出所有學生的 class：名稱以 Boid_ 開頭、實作 IBrain、有無參數建構子（名稱以 Boid_template 開頭的範本不參賽）
    // 任何一個學生的 class 有問題都只會被記到 rejected，不影響其他人
    public static class Roster
    {
        public const int MaxTeams = 10;

        public static List<IBrain> Find(Assembly assembly, out List<string> rejected)
        {
            rejected = new List<string>();
            var brains = new List<IBrain>();
            Type[] all;
            try { all = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { all = ex.Types.Where(t => t != null).ToArray(); }

            var candidates = all
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IBrain).IsAssignableFrom(t)
                    && t.Name.StartsWith("Boid_") && !t.Name.StartsWith("Boid_template"))
                .OrderBy(t => t.Name, StringComparer.Ordinal);

            foreach (Type type in candidates)
            {
                if (brains.Count >= MaxTeams)
                {
                    rejected.Add(type.Name + "：超過 " + MaxTeams + " 隊");
                    continue;
                }
                try
                {
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        rejected.Add(type.Name + "：沒有無參數建構子");
                        continue;
                    }
                    brains.Add((IBrain)Activator.CreateInstance(type));
                }
                catch (Exception ex)
                {
                    rejected.Add(type.Name + "：建立失敗 " + Short(ex));
                }
            }
            return brains;
        }

        public static string Short(Exception ex)
        {
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            string text = inner.GetType().Name + ": " + inner.Message;
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length > 80 ? text.Substring(0, 80) : text;
        }
    }
}
