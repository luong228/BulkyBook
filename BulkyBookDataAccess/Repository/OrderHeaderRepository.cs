using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkyBook.DataAccess.Repository
{
    public class OrderHeaderRepository : Repository<OrderHeader>, IOrderHeaderRepository
    {
        private readonly ApplicationDbContext _db;
        public OrderHeaderRepository(ApplicationDbContext db) : base(db)
        {
            _db = db;
        }


        public void Update(OrderHeader obj)
        {
            _db.OrderHeaders.Update(obj);
        }

		public void UpdateStatus(int id, string OrderStatus, string? PaymentStatus = null)
		{
            var orderFromDb = _db.OrderHeaders.FirstOrDefault(u => u.Id == id);
            if (orderFromDb != null)
            {
                orderFromDb.OrderStatus = OrderStatus;
                if (PaymentStatus != null)
                {
                    orderFromDb.PaymentStatus = PaymentStatus;
                }
            }
		}
		public void UpdateStripePaymentID(int id, string sessionId, string PaymnentIntentId)
		{
            var orderFromDb = _db.OrderHeaders.FirstOrDefault(u => u.Id == id);

            if (orderFromDb== null)
            {
                orderFromDb.SessionId = sessionId;
                orderFromDb.PaymentIntentId = PaymnentIntentId;
            }
		}
	}
}
