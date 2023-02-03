﻿using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;

namespace BulkyBookWeb.Areas.Customer.Controllers
{
	[Area("Customer")]
	[Authorize]
	public class CartController : Controller
	{
		private readonly IUnitOfWork _unitOfWork;
		[BindProperty]
		public ShoppingCartVM ShoppingCartVM { get; set; }
		public CartController(IUnitOfWork unitOfWork)
		{
			_unitOfWork = unitOfWork;
		}
		//[AllowAnonymous] - make this action method can be accessed by a user that is not authorized
		public IActionResult Index()
		{
			var claimsIdentity = (ClaimsIdentity?)User.Identity;
			var claim = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier);

			ShoppingCartVM = new ShoppingCartVM()
			{
				ListCart = _unitOfWork.ShoppingCartRepository.GetAll(u => u.ApplicationUserId == claim.Value,
				 includeProperties: "Product"),
				OrderHeader = new()
			};
			foreach (var cart in ShoppingCartVM.ListCart)
			{
				cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
				ShoppingCartVM.OrderHeader.OrderTotal += cart.Price * cart.Count;
			}
			return View(ShoppingCartVM);
		}
		public IActionResult Summary()
		{
			var claimsIdentity = (ClaimsIdentity?)User.Identity;
			var claim = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier);

			ShoppingCartVM = new ShoppingCartVM()
			{
				ListCart = _unitOfWork.ShoppingCartRepository.GetAll(u => u.ApplicationUserId == claim.Value,
				 includeProperties: "Product"),
				OrderHeader = new()
			};
			ShoppingCartVM.OrderHeader.ApplicationUser = _unitOfWork.ApplicationUserRepository.GetFirstOrDefault(
				u => u.Id == claim.Value
				);
			ShoppingCartVM.OrderHeader.Name = ShoppingCartVM.OrderHeader.ApplicationUser.Name;
			ShoppingCartVM.OrderHeader.PhoneNumber = ShoppingCartVM.OrderHeader.ApplicationUser.PhoneNumber;
			ShoppingCartVM.OrderHeader.StreetAddress = ShoppingCartVM.OrderHeader.ApplicationUser.StreetAddress;
			ShoppingCartVM.OrderHeader.City = ShoppingCartVM.OrderHeader.ApplicationUser.City;
			ShoppingCartVM.OrderHeader.State = ShoppingCartVM.OrderHeader.ApplicationUser.State;
			ShoppingCartVM.OrderHeader.PostalCode = ShoppingCartVM.OrderHeader.ApplicationUser.PostalCode;

			foreach (var cart in ShoppingCartVM.ListCart)
			{
				cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
				ShoppingCartVM.OrderHeader.OrderTotal += cart.Price * cart.Count;
			}
			return View(ShoppingCartVM);
		}
		[HttpPost]
		[ActionName("Summary")]
		[ValidateAntiForgeryToken]

		public IActionResult SummaryPOST()
		{
			var claimsIdentity = (ClaimsIdentity?)User.Identity;
			var claim = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier);
			ShoppingCartVM.ListCart = _unitOfWork.ShoppingCartRepository.GetAll(u => u.ApplicationUserId == claim.Value,
				 includeProperties: "Product");

			ShoppingCartVM.OrderHeader.OrderDate = System.DateTime.Now;
			ShoppingCartVM.OrderHeader.ApplicationUserId = claim.Value;

			foreach (var cart in ShoppingCartVM.ListCart)
			{
				cart.Price = GetPriceBasedOnQuantity(cart.Count, cart.Product.Price, cart.Product.Price50, cart.Product.Price100);
				ShoppingCartVM.OrderHeader.OrderTotal += cart.Price * cart.Count;
			}

			ApplicationUser applicationUser = _unitOfWork.ApplicationUserRepository.GetFirstOrDefault(u => u.Id == claim.Value);
			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusPending;
			} 
			else
			{
				ShoppingCartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayPayment;
				ShoppingCartVM.OrderHeader.OrderStatus = SD.StatusApproved;
			}
			//Create Order Header
			_unitOfWork.OrderHeaderRepository.Add(ShoppingCartVM.OrderHeader);
			_unitOfWork.Save();

			//Create Order Detail
			foreach (var cart in ShoppingCartVM.ListCart)
			{
				OrderDetail orderDetail = new()
				{
					ProductId = cart.ProductId,
					OrderId = ShoppingCartVM.OrderHeader.Id,
					Price = cart.Price,
					Count = cart.Count,
				};
				_unitOfWork.OrderDetailRepository.Add(orderDetail);
				//_unitOfWork.Save(); Redundant
			}
			_unitOfWork.Save();
			// If individual user
			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
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
					SuccessUrl = domain + $"customer/cart/OrderConfirmation?id={ShoppingCartVM.OrderHeader.Id}",
					CancelUrl = domain + "customer/cart/index",
				};
				foreach (var item in ShoppingCartVM.ListCart)
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

				_unitOfWork.OrderHeaderRepository.UpdateStripePaymentID(ShoppingCartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
				_unitOfWork.Save();

				Response.Headers.Add("Location", session.Url);
				return new StatusCodeResult(303);
			}
			else
			{
				return RedirectToAction("OrderConfirmation", "Cart", new { id = ShoppingCartVM.OrderHeader.Id});
			}
		}
		public IActionResult OrderConfirmation(int id)
		{
			OrderHeader orderHeader = _unitOfWork.OrderHeaderRepository.GetFirstOrDefault(u => u.Id == id);

			if (orderHeader == null)
			{
				return RedirectToAction("Index", "Home");
			}
			if (orderHeader.PaymentStatus != SD.PaymentStatusDelayPayment)
			{
				var service = new SessionService();
				Session session = service.Get(orderHeader.SessionId);
				// Check the stripe status
				if (session.PaymentStatus.ToLower() == "paid")
				{
					_unitOfWork.OrderHeaderRepository.UpdateStripePaymentID(id, session.Id, session.PaymentIntentId);
					_unitOfWork.OrderHeaderRepository.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
					//_unitOfWork.Save();
				}
			}
			else
			{
                orderHeader.PaymentDueDate = DateTime.Now.AddDays(30);
            }
			
			List<ShoppingCart> shoppingCarts = _unitOfWork.ShoppingCartRepository.GetAll(
				u => u.ApplicationUserId == orderHeader.ApplicationUserId
				).ToList();
			_unitOfWork.ShoppingCartRepository.RemoveRange(shoppingCarts);
			_unitOfWork.Save();

			return View(id);
		}
		public IActionResult Plus(int cartId)
		{
			var cart = _unitOfWork.ShoppingCartRepository.GetFirstOrDefault(u => u.Id == cartId);
			_unitOfWork.ShoppingCartRepository.IncrementCount(cart, 1);
			_unitOfWork.Save();
			return RedirectToAction(nameof(Index));
		}
		public IActionResult Minus(int cartId)
		{
			var cart = _unitOfWork.ShoppingCartRepository.GetFirstOrDefault(u => u.Id == cartId);
			if (cart.Count <= 1)
			{
				_unitOfWork.ShoppingCartRepository.Remove(cart);
			}
			else
			{
				_unitOfWork.ShoppingCartRepository.DecrementCount(cart, 1);
			}
			_unitOfWork.Save();
			return RedirectToAction(nameof(Index));
		}
		public IActionResult Remove(int cartId)
		{
			var cart = _unitOfWork.ShoppingCartRepository.GetFirstOrDefault(u => u.Id == cartId);
			_unitOfWork.ShoppingCartRepository.Remove(cart);
			_unitOfWork.Save();
			return RedirectToAction(nameof(Index));
		}
		private double GetPriceBasedOnQuantity(double quantity, double price, double price50, double price100)
		{
			if (quantity <= 50)
			{
				return price;
			}
			else
			{
				if (quantity <= 100)
				{
					return price50;
				}
				return price100;
			}
		}
	}
}