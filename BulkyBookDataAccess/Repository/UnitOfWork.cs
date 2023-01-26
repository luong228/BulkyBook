using BulkyBook.DataAccess.Repository.IRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BulkyBook.DataAccess.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _db;
        public UnitOfWork(ApplicationDbContext db)
        {
            _db = db;
            CategoryRepository = new CategoryRepository(_db);
            CoverTypeRepository = new CoverTypeRepository(_db);
            ProductRepository = new ProductRepository(_db);
        }
        public ICategoryRepository CategoryRepository { get; private set; }
        public ICoverTypeRepository CoverTypeRepository { get; private set; }
        public IProductRepository ProductRepository { get; private set; }

        public void Save()
        {
            _db.SaveChanges();
        }
    }
}
