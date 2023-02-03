using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
	[Area("Admin")]
	[Authorize]
	public class OrderController : Controller
	{
		private readonly IUnitOfWork _unitOfWork;
		[BindProperty]
		public OrderVM OrderVM { get; set; }

		public OrderController(IUnitOfWork unitOfWork)
		{
			_unitOfWork = unitOfWork;
		}

		public IActionResult Index()
		{
			return View();
		}
        public IActionResult Details(int orderId)
        {
			OrderVM = new()
			{
				OrderHeader = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == orderId, includeProperties: "ApplicationUser"),
				OrderDetail = _unitOfWork.OrderDetailRepository.GetAll(u => u.OrderId == orderId, includeProperties: "Product"),

			};
            return View(OrderVM);
        }
		[HttpPost]
		[ActionName("Details")]
		[ValidateAntiForgeryToken]
		public IActionResult Details_Pay_Now()
        {
			OrderVM.OrderHeader = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, includeProperties: "ApplicationUser");
			OrderVM.OrderDetail = _unitOfWork.OrderDetailRepository.GetAll(u => u.OrderId == OrderVM.OrderHeader.Id, includeProperties: "Product");

            //Stripe settings
            var domain = "https://localhost:44342/";
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string>
                    {
                        "card",
                    },
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
                SuccessUrl = domain + $"admin/order/PaymentConfirmation?orderId={OrderVM.OrderHeader.Id}",
                CancelUrl = domain + $"admin/order/details?orderId={OrderVM.OrderHeader.Id}",
            };
            foreach (var item in OrderVM.OrderDetail)
            {
                var sessionLineItem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(item.Price) * 100, // 20.00 -> 2000
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Product.Title,
                        },
                    },
                    Quantity = item.Count,
                };
                options.LineItems.Add(sessionLineItem);
            }
            var service = new SessionService();
            Session session = service.Create(options);

            //Update do not use unit Of Work
            //ShoppingCartVM.OrderHeader.SessionId = session.Id;
            //ShoppingCartVM.OrderHeader.PaymentIntentId = session.PaymentIntentId;

            _unitOfWork.OrderHeaderRepository.UpdateStripePaymentID(OrderVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();

            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);

        }
        public IActionResult PaymentConfirmation(int orderId)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == orderId);

            if (orderHeader == null)
            {
                return RedirectToAction("Details", "Order");
            }
            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayPayment)
            {
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);
                // Check the stripe status
                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeaderRepository.UpdateStripePaymentID(orderId, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeaderRepository.UpdateStatus(orderId, orderHeader.OrderStatus, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }

            return View(orderId);
        }
        [HttpPost]
		[Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
		[ValidateAntiForgeryToken]
		public IActionResult UpdateOrderDetail()
        {

			var orderHeaderFromDb = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked:false);

			orderHeaderFromDb.Name = OrderVM.OrderHeader.Name;
			orderHeaderFromDb.PhoneNumber = OrderVM.OrderHeader.PhoneNumber;
			orderHeaderFromDb.StreetAddress = OrderVM.OrderHeader.StreetAddress;
			orderHeaderFromDb.City= OrderVM.OrderHeader.City;
			orderHeaderFromDb.State= OrderVM.OrderHeader.State;
			orderHeaderFromDb.PostalCode= OrderVM.OrderHeader.PostalCode;

			if(OrderVM.OrderHeader.Carrier != null)
			{
				orderHeaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
			}
			if (OrderVM.OrderHeader.TrackingNumber != null)
			{
				orderHeaderFromDb.TrackingNumber= OrderVM.OrderHeader.TrackingNumber;
			}

			_unitOfWork.OrderHeaderRepository.Update(orderHeaderFromDb);
			_unitOfWork.Save();

			TempData["Success"] = "Order Details Update Successfully";
            return RedirectToAction("Details", "Order", new { orderId = orderHeaderFromDb.Id});
        }
        [HttpPost]
		[Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
		[ValidateAntiForgeryToken]
        public IActionResult StartProcessing()
        {    
            _unitOfWork.OrderHeaderRepository.UpdateStatus(OrderVM.OrderHeader.Id, SD.StatusInProcess);
            _unitOfWork.Save();

            TempData["Success"] = "Order Status Update Successfully";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }
		[HttpPost]
		[Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
		[ValidateAntiForgeryToken]
        public IActionResult ShipOrder()
        {
			var orderHeaderFromDb = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);
			orderHeaderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
			orderHeaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
			orderHeaderFromDb.OrderStatus = SD.StatusShipped;
			orderHeaderFromDb.ShippingDate = DateTime.Now;
			_unitOfWork.OrderHeaderRepository.Update(orderHeaderFromDb);
            _unitOfWork.Save();

            TempData["Success"] = "Order Shipped Successfully";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }
		[HttpPost]
		[Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
		[ValidateAntiForgeryToken]
        public IActionResult CancelOrder()
        {
			var orderHeaderFromDb = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == OrderVM.OrderHeader.Id, tracked: false);

			if (orderHeaderFromDb.PaymentStatus == SD.PaymentStatusApproved)
			{
				var options = new RefundCreateOptions()
				{
					Reason = RefundReasons.RequestedByCustomer,
					PaymentIntent = orderHeaderFromDb.PaymentIntentId,
				};
				var service = new RefundService();
				Refund refund = service.Create(options);
				_unitOfWork.OrderHeaderRepository.UpdateStatus(orderHeaderFromDb.Id, SD.StatusCancelled, SD.StatusRefunded);
			}
			else
			{
				_unitOfWork.OrderHeaderRepository.UpdateStatus(orderHeaderFromDb.Id, SD.StatusCancelled, SD.StatusCancelled);
			}
            _unitOfWork.Save();

            TempData["Success"] = "Order Cancelled Successfully";
            return RedirectToAction("Details", "Order", new { orderId = OrderVM.OrderHeader.Id });
        }
        #region API CALLS
        [HttpGet]
		public IActionResult GetAll(string status)
		{
			IEnumerable<OrderHeader> orderHeaders;
			
			if(User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
			{
			orderHeaders = _unitOfWork.OrderHeaderRepository.GetAll(includeProperties: "ApplicationUser");
			}
			else
			{
				var claimsIdentity = (ClaimsIdentity)User.Identity;
				var claim = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier);
				orderHeaders = _unitOfWork.OrderHeaderRepository.GetAll(u => u.ApplicationUserId == claim.Value, includeProperties: "ApplicationUser");
			}

			
			switch(status)
			{
                case "pending":
					orderHeaders = orderHeaders.Where(u => u.PaymentStatus == SD.PaymentStatusDelayPayment || u.OrderStatus == SD.StatusPending);
                    break;
				case "inprocess":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusInProcess);
                    break;
                case "completed":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusShipped);
                    break;
				case "approved":
                    orderHeaders = orderHeaders.Where(u => u.OrderStatus == SD.StatusApproved);
                    break;
				default:
                    break;
            }

			return Json(new { data = orderHeaders });

		}
		#endregion
	}
}
