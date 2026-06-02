using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
namespace thuvienso.Hubs;

public class PaymentHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        // Lấy userId từ Query String truyền từ JS
        var userId = Context.GetHttpContext()?.Request.Query["userId"].ToString();

        if (!string.IsNullOrEmpty(userId))
        {
            // Lưu ID này vào Group để sau này server biết user nào đang ngồi ở đâu
            await Groups.AddToGroupAsync(Context.ConnectionId, "User_" + userId);
        }

        await base.OnConnectedAsync();
    }
}
