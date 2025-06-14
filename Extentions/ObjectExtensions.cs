using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AvalonFlow
{
    public static class ObjectExtensions
    {
        /// <summary>
        /// Obtiene el valor de una propiedad de un objeto de forma segura
        /// </summary>
        /// <typeparam name="T">Tipo de dato esperado</typeparam>
        /// <param name="obj">Objeto del cual obtener el valor</param>
        /// <param name="propertyName">Nombre de la propiedad</param>
        /// <returns>Valor convertido al tipo especificado o default(T) si no existe o no se puede convertir</returns>
        public static T GetValue<T>(this object obj, string propertyName)
        {
            if (obj == null) return default(T);

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null || !property.CanRead) return default(T);

            try
            {
                var value = property.GetValue(obj, null);
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default(T);
            }
        }

        /// <summary>
        /// Versión con valor por defecto personalizado
        /// </summary>
        public static T GetValue<T>(this object obj, string propertyName, T defaultValue)
        {
            if (obj == null) return defaultValue;

            var property = obj.GetType().GetProperty(propertyName);
            if (property == null || !property.CanRead) return defaultValue;

            try
            {
                var value = property.GetValue(obj, null);
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
    }
}
