﻿using Infrastructure;
using Infrastructure.Attribute;
using Infrastructure.Extensions;
using Infrastructure.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Snowflake.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ZR.Admin.WebApi.Extensions;
using ZR.Admin.WebApi.Filters;
using ZR.Common;
using ZR.Model.System;
using ZR.Service.System.IService;

namespace ZR.Admin.WebApi.Controllers
{
    /// <summary>
    /// 公共模块
    /// </summary>
    [Route("[controller]/[action]")]
    public class CommonController : BaseController
    {
        private OptionsSetting OptionsSetting;
        private NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        private IWebHostEnvironment WebHostEnvironment;
        private ISysFileService SysFileService;
        public CommonController(IOptions<OptionsSetting> options, IWebHostEnvironment webHostEnvironment, ISysFileService fileService)
        {
            WebHostEnvironment = webHostEnvironment;
            SysFileService = fileService;
            OptionsSetting = options.Value;
        }

        /// <summary>
        /// hello
        /// </summary>
        /// <returns></returns>
        [Route("/")]
        [HttpGet]
        public IActionResult Index()
        {
            return Content("Hello看到这里页面说明你已经成功启动了本项目，加油吧 少年。");
        }

        /// <summary>
        /// 发送邮件
        /// </summary>
        /// <param name="sendEmailVo">请求参数接收实体</param>
        /// <returns></returns>
        [ActionPermissionFilter(Permission = "tool:email:send")]
        [Log(Title = "发送邮件", IsSaveRequestData = false)]
        [HttpPost]
        public IActionResult SendEmail([FromBody] SendEmailDto sendEmailVo)
        {
            if (sendEmailVo == null || string.IsNullOrEmpty(sendEmailVo.Subject) || string.IsNullOrEmpty(sendEmailVo.ToUser))
            {
                return ToResponse(ApiResult.Error($"请求参数不完整"));
            }
            if (string.IsNullOrEmpty(OptionsSetting.MailOptions.From) || string.IsNullOrEmpty(OptionsSetting.MailOptions.Password))
            {
                return ToResponse(ApiResult.Error($"请配置邮箱信息"));
            }

            MailHelper mailHelper = new();

            string[] toUsers = sendEmailVo.ToUser.Split(",", StringSplitOptions.RemoveEmptyEntries);
            if (sendEmailVo.SendMe)
            {
                toUsers.Append(mailHelper.FromEmail);
            }
            mailHelper.SendMail(toUsers, sendEmailVo.Subject, sendEmailVo.Content, sendEmailVo.FileUrl, sendEmailVo.HtmlContent);

            logger.Info($"发送邮件{JsonConvert.SerializeObject(sendEmailVo)}");

            return SUCCESS(true);
        }

        #region 上传

        /// <summary>
        /// 存储文件
        /// </summary>
        /// <param name="formFile"></param>
        /// <param name="fileDir">存储目录</param>
        /// <param name="fileName">自定义文件名</param>
        /// <param name="uploadType">上传类型 1、发送邮件</param>
        /// <returns></returns>
        [HttpPost()]
        [Verify]
        [ActionPermissionFilter(Permission = "common")]
        public async Task<IActionResult> UploadFile([FromForm(Name = "file")] IFormFile formFile, string fileName = "", string fileDir = "uploads", int uploadType = 0)
        {
            if (formFile == null) throw new CustomException(ResultCode.PARAM_ERROR, "上传文件不能为空");

            SysFile file = await SysFileService.SaveFileLocal(WebHostEnvironment.WebRootPath, fileName, fileDir, HttpContext.GetName(), formFile); 
            return SUCCESS(new
            {
                url = uploadType == 1 ? file.FileUrl : file.AccessUrl,
                fileName,
                fileId = file.Id.ToString()
            });
        }

        /// <summary>
        /// 存储文件到阿里云
        /// </summary>
        /// <param name="formFile"></param>
        /// <param name="fileName">自定义文件名</param>
        /// <param name="fileDir">上传文件夹路径</param>
        /// <returns></returns>
        [HttpPost]
        [Verify]
        [ActionPermissionFilter(Permission = "common")]
        public async Task<IActionResult> UploadFileAliyun([FromForm(Name = "file")] IFormFile formFile, string fileName = "", string fileDir = "")
        {
            if (fileDir.IsEmpty()) fileDir = "uploads";
            if (formFile == null) throw new CustomException(ResultCode.PARAM_ERROR, "上传文件不能为空");
            string fileExt = Path.GetExtension(formFile.FileName);//文件后缀
            double fileSize = formFile.Length / 1024.0;//文件大小KB
            string[] NotAllowedFileExtensions = new string[] { ".bat", ".exe", ".jar", ".js" };
            int MaxContentLength = 15;
            if (NotAllowedFileExtensions.Contains(fileExt))
            {
                return ToResponse(ResultCode.CUSTOM_ERROR, "上传失败，未经允许上传类型");
            }
            if ((fileSize / 1024) > MaxContentLength)
            {
                return ToResponse(ResultCode.CUSTOM_ERROR, "上传文件过大，不能超过 " + MaxContentLength + " MB");
            }

            (bool, string, string) result = new();
            await Task.Run(() =>
            {
                result = SysFileService.SaveFile(fileDir, formFile, fileName, "");
            });
            if (!result.Item1)
            {
                return ToResponse(ApiResult.Error("阿里云连接失败"));
            }
            long id = await SysFileService.InsertFile(new(formFile.FileName, fileName, fileExt, fileSize + "kb", "", result.Item2, HttpContext.GetName())
            {
                StoreType = (int)Infrastructure.Enums.StoreType.ALIYUN,
                FileType = formFile.ContentType
            });
            return SUCCESS(new
            {
                url = result.Item2,
                fileName = result.Item3,
                fileId = id
            });
        }
        #endregion
    }
}
