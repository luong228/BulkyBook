using BulkyBook.DataAccess;
using BulkyBook.DataAccess.Repository.IRepository;
using BulkyBook.Models;
using BulkyBook.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Rendering;
using BulkyBook.Utility;
using Microsoft.AspNetCore.Authorization;

namespace BulkyBookWeb.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class ProductController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWebHostEnvironment _hostEnvironment;
        public ProductController(IUnitOfWork unitOfWork, IWebHostEnvironment hostEnvironment)
        {
            _unitOfWork = unitOfWork;
            _hostEnvironment = hostEnvironment;
        }

        public IActionResult Index()
        {

            return View();
        }

        public IActionResult Upsert(int? id)
        {
            //ViewModel Used
            ProductVM productVM = new()
            {
                Product = new(),
                CategoryList = _unitOfWork.CategoryRepository.GetAll().Select(
                u => new SelectListItem
                {
                    Text = u.Name,
                    Value = u.Id.ToString(),
                }),
                CoverTypeList = _unitOfWork.CoverTypeRepository.GetAll().Select(
              u => new SelectListItem
              {
                  Text = u.Name,
                  Value = u.Id.ToString(),
              })

            };
            //ViewBag Used
            //IEnumerable<SelectListItem> CategoryList = _unitOfWork.CategoryRepository.GetAll().Select(
            //u => new SelectListItem
            //{
            //    Text = u.Name,
            //    Value = u.Id.ToString();
            //}
            //)
            if (id == null || id == 0)
            {
                //Create Product
                //ViewBag.CategoryList = CategoryList;
                //ViewData["CoverTypeList"] = CoverTypeList;
                return View(productVM);
            }
            else
            {
                productVM.Product = _unitOfWork.ProductRepository.GetFirstOrDefault(u => u.Id == id);
                return View(productVM);
                // Update Product
            }
        }

        //POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upsert(ProductVM obj, IFormFile? file)
        {
            if (ModelState.IsValid)
            {
                string wwwRootPath = _hostEnvironment.WebRootPath;
                if (file != null)
                {
                    string fileName = Guid.NewGuid().ToString();
                    var uploads = Path.Combine(wwwRootPath, @"images\products");
                    var extension = Path.GetExtension(file.FileName);

                    // Delete old image if exist
                    if (obj.Product.ImageUrl != null)
                    {
                        var test = obj.Product.ImageUrl.TrimStart('\\');
                        var oldImagePath = Path.Combine(wwwRootPath, obj.Product.ImageUrl.TrimStart('\\'));
                        if (System.IO.File.Exists(oldImagePath))
                        {
                            System.IO.File.Delete(oldImagePath);
                        }
                    }

                    using (var fileStreams = new FileStream(Path.Combine(uploads, fileName + extension), FileMode.Create))
                    {
                        //Copy content from file to fileStream that already created
                        file.CopyTo(fileStreams);
                    }
                    obj.Product.ImageUrl = @"\images\products\" + fileName + extension;
                }
                if (obj.Product.Id == 0)
                {
                    _unitOfWork.ProductRepository.Add(obj.Product);
                    TempData["success"] = "Product created successfully";
                }
                else
                {
                    _unitOfWork.ProductRepository.Update(obj.Product);
                    TempData["success"] = "Product updated successfully";
                }
                _unitOfWork.Save();
                return RedirectToAction("Index");
            }
            return View(obj);
        }

        #region API CALLS
        [HttpGet]
        public IActionResult GetAll()
        {
            var productList = _unitOfWork.ProductRepository.GetAll(includeProperties: "Category,CoverType");
            return Json(new { data = productList });

        }
        //POST
        [HttpDelete]
        public IActionResult Delete(int? id)
        {
            var obj = _unitOfWork.ProductRepository.GetFirstOrDefault(u => u.Id == id);
            if (obj == null)
            {
                return Json(new { success = false, message = "Error while deleting" });
            }
            var oldImagePath = Path.Combine(_hostEnvironment.WebRootPath, obj.ImageUrl.TrimStart('\\'));
            if (System.IO.File.Exists(oldImagePath))
            {
                System.IO.File.Delete(oldImagePath);
            }
            _unitOfWork.ProductRepository.Remove(obj);
            _unitOfWork.Save();
            return Json(new { success = true, message = "Product deleted successfully" });
        }
        #endregion
    }
}
