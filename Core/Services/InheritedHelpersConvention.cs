using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Linq;

namespace Core.Services;

/// <summary>
/// MVC считает действием любой публичный метод контроллера, включая унаследованные.
/// Вспомогательные методы базовых контроллеров (HostStreamProxy, RedirectToPlay и т.д.)
/// маршрутных атрибутов не имеют и действиями быть не должны.
/// </summary>
public sealed class InheritedHelpersConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            for (int i = controller.Actions.Count - 1; i >= 0; i--)
            {
                var action = controller.Actions[i];

                if (action.ActionMethod.DeclaringType == controller.ControllerType.AsType())
                    continue;

                if (action.Attributes.Any(a => a is IRouteTemplateProvider or IActionHttpMethodProvider))
                    continue;

                controller.Actions.RemoveAt(i);
            }
        }
    }
}
