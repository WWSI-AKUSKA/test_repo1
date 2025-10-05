using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Demo.Tests
{
    public class PersonTests
    {
        private static Type GetPersonType()
        {
            // 1) Najpierw spróbuj pełnej nazwy typu w projekcie aplikacji
            var preferred = Type.GetType("Demo.App.Person, Demo.App", throwOnError: false);
            if (preferred != null) return preferred;

            // 2) Spróbuj kilku wariantów (gdy assembly lub namespace mają inne nazwy)
            string[] candidateTypeNames = { "Demo.App.Person", "Demo.Person", "Person" };
            string[] candidateAssemblies = { "Demo.App", "Demo", null! }; // null => nie podawaj assembly

            foreach (var typeName in candidateTypeNames)
            {
                foreach (var asm in candidateAssemblies)
                {
                    var full = asm is null ? typeName : $"{typeName}, {asm}";
                    var t = Type.GetType(full, throwOnError: false);
                    if (t != null && t.IsClass && t.IsPublic) return t;
                }
            }

            // 3) Fallback: przeskanuj wszystkie załadowane assembly
            var scanned = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); } // bezpiecznie gdy ReflectionTypeLoadException
                })
                .FirstOrDefault(x => x.IsClass && x.IsPublic && x.Name == "Person");

            if (scanned != null) return scanned;

            // 4) Przyjazny komunikat diagnostyczny
            throw new InvalidOperationException(
                "Nie znaleziono publicznej klasy 'Person'. Upewnij się, że projekt testów referencjonuje Demo.App " +
                "(ProjectReference w Demo.Tests.csproj) i że klasa ma namespace 'Demo.App' oraz jest public.");
        }
        private static bool IsNumeric(Type t) =>
           t == typeof(decimal) || t == typeof(double) || t == typeof(float) ||
           t == typeof(long) || t == typeof(int) || t == typeof(short);

        private static object ConvertTo(Type t, int value)
        {
            if (t == typeof(int)) return value;
            if (t == typeof(long)) return (long)value;
            if (t == typeof(short)) return (short)value;
            if (t == typeof(float)) return (float)value;
            if (t == typeof(double)) return (double)value;
            if (t == typeof(decimal)) return (decimal)value;
            throw new InvalidOperationException($"Nieobsługiwany typ liczbowy: {t.Name}");
        }

        private static int ToInt(object? v) => v switch
        {
            int i => i,
            long l => (int)l,
            short s => s,
            float f => (int)f,
            double d => (int)d,
            decimal m => (int)m,
            _ => throw new InvalidOperationException("Właściwość Age nie jest typem liczbowym.")
        };

        private static object CreatePersonInstance(Type personType)
        {
          
            var p = Activator.CreateInstance(personType);
            if (p != null) return p;

    
            var ctor = personType.GetConstructors()
                     .OrderBy(c => c.GetParameters().Length)
                     .FirstOrDefault() ?? throw new InvalidOperationException("Brak dostępnego konstruktora Person.");

            var args = ctor.GetParameters().Select(param =>
            {
                var t = param.ParameterType;
                if (t == typeof(string)) return "";
                if (t == typeof(bool)) return false;
                if (IsNumeric(t)) return ConvertTo(t, 0);
                if (t == typeof(DateTime)) return DateTime.MinValue;
                return t.IsValueType ? Activator.CreateInstance(t)! : null!;
            }).ToArray();

            return ctor.Invoke(args);
        }

        [Fact]
        public void Class_Person_Should_Exist()
        {
            var t = GetPersonType();
            Assert.NotNull(t);
        }

        [Theory]
        [InlineData("FirstName")]
        [InlineData("LastName")]
        [InlineData("Age")]
        public void Required_Properties_Should_Exist(string propertyName)
        {
            var t = GetPersonType();
            var prop = t.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.True(prop != null && prop.CanRead, $"Właściwość {propertyName} musi istnieć i mieć getter.");
        }

        [Fact]
        public void Methods_Should_Exist()
        {
            var t = GetPersonType();
     
            var getFullName = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                               .FirstOrDefault(m => m.Name == "GetFullName" && m.GetParameters().Length == 0);
            Assert.NotNull(getFullName);

            var haveBirthday = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                .FirstOrDefault(m =>
                                    m.Name == "HaveBirthday" &&
                                    (m.GetParameters().Length == 0 ||
                                     (m.GetParameters().Length == 1 && IsNumeric(m.GetParameters()[0].ParameterType))));
            Assert.NotNull(haveBirthday);

       
            var rename = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                          .FirstOrDefault(m =>
                          {
                              if (m.Name != "Rename") return false;
                              var ps = m.GetParameters();
                              return ps.Length == 2 &&
                                     ps[0].ParameterType == typeof(string) &&
                                     ps[1].ParameterType == typeof(string);
                          });
            Assert.NotNull(rename);
        }


        [Fact]
        public void GetFullName_Should_Contain_First_And_Last_Name()
        {
            var t = GetPersonType();
            var p = CreatePersonInstance(t);

      
            var first = t.GetProperty("FirstName");
            var last = t.GetProperty("LastName");
            Assert.NotNull(first);
            Assert.NotNull(last);

            if (first!.CanWrite) first.SetValue(p, "Ala");
            if (last!.CanWrite) last.SetValue(p, "Makota");

            var getFullName = t.GetMethod("GetFullName", BindingFlags.Public | BindingFlags.Instance)!;
            var result = getFullName.Invoke(p, null) as string ?? "";
            var normalized = result.ToLowerInvariant();

            Assert.Contains("ala", normalized);
            Assert.Contains("makota", normalized);
        }

        [Fact]
        public void HaveBirthday_Should_Increase_Age()
        {
            var t = GetPersonType();
            var p = CreatePersonInstance(t);

            var ageProp = t.GetProperty("Age", BindingFlags.Public | BindingFlags.Instance)
                         ?? throw new InvalidOperationException("Brak właściwości Age.");
            // ustaw start na 20, jeśli można
            if (ageProp.CanWrite) ageProp.SetValue(p, ConvertTo(ageProp.PropertyType, 20));

            var before = ToInt(ageProp.GetValue(p));

            var m = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .First(mi => mi.Name == "HaveBirthday");
            var ps = m.GetParameters();

            if (ps.Length == 0)
            {
                m.Invoke(p, null);      
                Assert.Equal(before + 1, ToInt(ageProp.GetValue(p)));
            }
            else
            {
            
                var arg = ConvertTo(ps[0].ParameterType, 1);
                m.Invoke(p, new[] { arg });
                Assert.Equal(before + 1, ToInt(ageProp.GetValue(p)));
            }
        }

        [Fact]
        public void Rename_Should_Change_Names()
        {
            var t = GetPersonType();
            var p = CreatePersonInstance(t);

            var rename = t.GetMethod("Rename", BindingFlags.Public | BindingFlags.Instance)
                        ?? throw new InvalidOperationException("Brak metody Rename.");

            rename.Invoke(p, new object[] { "Jan", "Kowalski" });

            var first = t.GetProperty("FirstName")?.GetValue(p)?.ToString() ?? "";
            var last = t.GetProperty("LastName")?.GetValue(p)?.ToString() ?? "";

            Assert.Equal("Jan", first);
            Assert.Equal("Kowalski", last);
        }
    }


}
