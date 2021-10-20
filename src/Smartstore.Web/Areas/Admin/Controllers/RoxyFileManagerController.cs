﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dasync.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Smartstore.Core.Content.Media;
using Smartstore.Core.Localization;
using Smartstore.Core.Security;
using Smartstore.Engine;
using Smartstore.IO;
using Smartstore.Utilities;
using Smartstore.Web.Controllers;

namespace Smartstore.Admin.Controllers
{
    public class RoxyFileManagerController : AdminController
    {
        private const int BufferSize = 32768;
        private const string ConfigFilePath = "~/lib/roxyfm/conf.json";

        private string _fileRoot = null;
        private Dictionary<string, string> _lang = null;
        private Dictionary<string, string> _roxySettings = null;

        private readonly IFileSystem _webRoot;
        private readonly IMediaUrlGenerator _mediaUrlGenerator;
        private readonly IMediaTypeResolver _mediaTypeResolver;
        private readonly MediaHelper _mediaHelper;
        private readonly MediaServiceFileSystemAdapter _fileSystem;
        private readonly ILocalizationFileResolver _locFileResolver;

        private readonly AlbumInfo _album;

        public RoxyFileManagerController(
            IApplicationContext appContext,
            IMediaService mediaService,
            IMediaUrlGenerator mediaUrlGenerator,
            IMediaSearcher mediaSearcher,
            IFolderService folderService,
            IAlbumRegistry albumRegistry,
            IMediaTypeResolver mediaTypeResolver,
            IMediaStorageConfiguration mediaStorageConfiguration,
            MediaHelper mediaHelper,
            MediaExceptionFactory exceptionFactory,
            ILocalizationFileResolver locFileResolver)
        {
            _mediaUrlGenerator = mediaUrlGenerator;
            _mediaTypeResolver = mediaTypeResolver;
            _mediaHelper = mediaHelper;
            _locFileResolver = locFileResolver;
            _webRoot = appContext.WebRoot;

            _album = albumRegistry.GetAlbumByName(SystemAlbumProvider.Files);
            _fileRoot = _album.Name;
            _fileSystem = new MediaServiceFileSystemAdapter(
                mediaService, 
                mediaSearcher, 
                folderService, 
                mediaStorageConfiguration, 
                mediaHelper, 
                exceptionFactory);
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost, IgnoreAntiforgeryToken]
        [Permission(Permissions.Media.Upload)]
        public async Task<IActionResult> ProcessRequest(
            string a = null /* action */,
            string f = null /* file */,
            string d = null /* dir */,
            string n = null /* target / name */,
            string type = null,
            string ext = null /* external */,
            string method = null)
        {
            var action = (a.NullEmpty() ?? "DIRLIST").ToUpper();

            try
            {
                return action switch
                {
                    "DIRLIST" => await ListDirTreeAsync(type),
                    "FILESLIST" => await ListFilesAsync(d, type),
                    "COPYDIR" => await CopyDirAsync(d, n),
                    "COPYFILE" => await CopyFileAsync(f, n),
                    "CREATEDIR" => await CreateDirAsync(d, n),
                    "DELETEDIR" => await DeleteDirAsync(d),
                    "DELETEFILE" => await DeleteFileAsync(f),
                    "DOWNLOAD" => await DownloadFileAsync(f),
                    //case "DOWNLOADDIR":
                    //    return await DownloadDirAsync(d);
                    "MOVEDIR" => await MoveDirAsync(d, n),
                    "MOVEFILE" => await MoveFileAsync(f, n),
                    "RENAMEDIR" => await RenameDirAsync(d, n),
                    "RENAMEFILE" => await RenameFileAsync(f, n),
                    //case "UPLOAD":
                    //    return await UploadAsync(d, ext.ToBool());
                    _ => Json(GetResultMessage("This action is not implemented.", "error")),
                };
            }
            catch (Exception ex)
            {
                Logger.ErrorsAll(ex);

                if (action == "UPLOAD")
                {
                    if (IsAjaxUpload(method))
                    {
                        return Json(GetResultMessage(LangRes("E_UploadNoFiles"), "error"));
                    }
                    else
                    {
                        var content = "<script>";
                        content += "parent.fileUploaded(" + GetResultString(LangRes("E_UploadNoFiles"), "error") + "); ";
                        content += "</script>";

                        return Content(content);
                    }
                }
                else
                {
                    return Json(GetResultMessage(ex.Message, "error"));
                }
            }
        }

        #region File system

        private string FileRoot
        {
            get
            {
                if (_fileRoot == null)
                {
                    _fileRoot = GetSetting("FILES_ROOT");

                    var sessionPathKey = GetSetting("SESSION_PATH_KEY");
                    if (sessionPathKey.HasValue())
                        _fileRoot = HttpContext.Session.GetString(sessionPathKey);
                        
                    if (_fileRoot.IsEmpty())
                        _fileRoot = _album.Name;
                }

                return _fileRoot;
            }
        }

        private string GetRelativePath(string path)
        {
            if (path.IsWebUrl())
            {
                var uri = new Uri(path);
                path = uri.PathAndQuery;
            }
            
            return (_fileSystem.MapUrlToStoragePath(path) ?? path).TrimStart('/', '\\');
        }

        private IAsyncEnumerable<IFile> GetFilesAsync(string path, string type)
        {
            var files = _fileSystem.EnumerateFilesAsync(GetRelativePath(path));

            if (type.IsEmpty() || type == "#")
            {
                return files;
            }

            type = type.ToLowerInvariant();
            bool predicate(IFile x)
            {
                return _mediaTypeResolver.Resolve(x.Extension) == type;
            }

            return files.Where(predicate);
        }

        private long CountFiles(string path, string type)
        {
            if (_fileSystem.IsCloudStorage)
            {
                // Dont't count, it's expensive!
                return 0;
            }

            Func<string, bool> predicate = null;

            if (type.HasValue() && type != "#")
            {
                type = type.ToLowerInvariant();
                predicate = x => _mediaTypeResolver.Resolve(Path.GetExtension(x)) == type;
            }

            return _fileSystem.CountFiles(path, "*", predicate, false);
        }

        private async Task<JsonResult> ListDirTreeAsync(string type)
        {
            var root = await _fileSystem.GetDirectoryAsync(FileRoot);
            var folders = await ListDirsAsync(FileRoot);
            var rootName = FileRoot;

            if (root is MediaFolderInfo fi && fi.Node.Value.ResKey.HasValue())
            {
                rootName = T(fi.Node.Value.ResKey);
            }

            folders.Insert(0, new RoxyFolder
            {
                Folder = root,
                DisplayName = rootName,
                SubFolders = folders.Count
            });

            var result = folders
                .Select(x =>
                {
                    var numFiles = CountFiles(x.Folder.SubPath, type);
                    return new
                    {
                        p = x.Folder.SubPath.Replace('\\', '/'),
                        n = x.DisplayName,
                        f = numFiles,
                        d = x.SubFolders
                    };
                })
                .ToArray();

            return Json(result);
        }

        private async Task<List<RoxyFolder>> ListDirsAsync(string path)
        {
            var result = new List<RoxyFolder>();

            await foreach (var dir in _fileSystem.EnumerateDirectoriesAsync(path))
            {
                var subDirs = await ListDirsAsync(dir.SubPath);

                result.Add(new RoxyFolder
                {
                    Folder = dir,
                    SubFolders = subDirs.Count
                });

                result.AddRange(subDirs);
            }

            return result;
        }

        private async Task<JsonResult> ListFilesAsync(string path, string type)
        {
            var files = GetFilesAsync(GetRelativePath(path), type);

            var result = await files.Select(file => new
            {
                p = _mediaUrlGenerator.GenerateUrl(file as MediaFileInfo, null, string.Empty),
                t = file.LastModified.ToUnixTime().ToString(),
                m = GetMimeType(file),
                s = file.Length.ToString(),
                w = file.Size.Width.ToString(),
                h = file.Size.Height.ToString()
            }).ToArrayAsync();

            return Json(result);
        }

        private async Task<IActionResult> DownloadFileAsync(string path)
        {
            path = GetRelativePath(path);

            var file = await _fileSystem.GetFileAsync(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"File {path} does not exist.", path);
            }

            return File(await file.OpenReadAsync(), GetMimeType(file), file.Name);
        }

        // TODO: DownloadDir

        private async Task<JsonResult> RenameDirAsync(string path, string name)
        {
            path = GetRelativePath(path);
            var newPath = PathUtility.Combine(Path.GetDirectoryName(path), name);
            await _fileSystem.MoveEntryAsync(path, newPath);
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> MoveFileAsync(string path, string newPath)
        {
            await _fileSystem.MoveEntryAsync(GetRelativePath(path), GetRelativePath(newPath));
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> RenameFileAsync(string path, string newName)
        {
            path = GetRelativePath(path);

            var fileType = Path.GetExtension(newName);
            if (!IsAllowedFileType(fileType))
            {
                throw new Exception(LangRes("E_FileExtensionForbidden"));
            }

            var dir = await _fileSystem.GetDirectoryForFileAsync(path);
            var newPath = PathUtility.Combine(dir.SubPath, newName);

            await _fileSystem.MoveEntryAsync(path, newPath);
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> MoveDirAsync(string path, string newPath)
        {
            newPath = PathUtility.Combine(GetRelativePath(newPath), Path.GetFileName(path));
            await _fileSystem.MoveEntryAsync(GetRelativePath(path), newPath);
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> CopyFileAsync(string path, string newPath)
        {
            path = GetRelativePath(path);

            var file = await _fileSystem.GetFileAsync(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"File {path} does not exist.", path);
            }

            newPath = PathUtility.Combine(GetRelativePath(newPath), file.Name);

            if ((await _fileSystem.CheckUniqueFileNameAsync(newPath)).Out(out var uniquePath))
            {
                newPath = uniquePath;
            }

            await file.CopyToAsync(newPath, false);
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> DeleteFileAsync(string path)
        {
            path = GetRelativePath(path);
            var file = await _fileSystem.GetFileAsync(path);
            if (!file.Exists)
            {
                throw new FileNotFoundException($"File {path} does not exist.", path);
            }

            await file.DeleteAsync();
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> DeleteDirAsync(string path)
        {
            path = GetRelativePath(path);

            if (path == FileRoot)
            {
                throw new Exception(LangRes("E_CannotDeleteRoot"));
            }

            var dir = await _fileSystem.GetDirectoryAsync(path);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Directory {path} does not exist.");
            }

            await dir.DeleteAsync();
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> CreateDirAsync(string path, string name)
        {
            path = GetRelativePath(path);
            path = PathUtility.Combine(path, name);
            
            var dir = await _fileSystem.GetDirectoryAsync(path);
            if (dir.Exists)
            {
                throw new Exception(LangRes("E_DirAlreadyExists"));
            }

            await dir.CreateAsync();
            return Json(GetResultMessage());
        }

        private async Task<JsonResult> CopyDirAsync(string path, string targetPath)
        {
            path = GetRelativePath(path);
            targetPath = GetRelativePath(targetPath);
            await _fileSystem.CopyDirectoryAsync(path, targetPath, false);
            return Json(GetResultMessage());
        }

        // TODO: UploadAsync

        #endregion

        #region Utilities

        private Task WriteResultAsync(string message = null, string type = "ok")
        {
            return WriteAsync(GetResultMessage(message, type));
        }

        private Task WriteAsync(object obj)
        {
            return Response.WriteAsJsonAsync(obj);
        }

        private static string GetResultString(string message = null, string type = "ok")
        {
            return JsonConvert.SerializeObject(GetResultMessage(message, type), new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
        }

        private static object GetResultMessage(string message = null, string type = "ok")
        {
            return new
            {
                res = type,
                msg = message.EmptyNull().Replace("\"", "\\\"")
            };
        }

        private Dictionary<string, string> ParseJson(string path)
        {
            try
            {
                var js = _webRoot.ReadAllText(path, Encoding.UTF8);
                var objStart = js.IndexOf('{');
                var json = js[objStart..];

                return JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                ex.Dump();
                return new Dictionary<string, string>();
            }
        }

        private string LangRes(string name)
        {
            if (_lang == null)
            {
                var locFile = _locFileResolver
                    .Resolve(Services.WorkContext.WorkingLanguage.UniqueSeoCode, "~/lib/roxyfm/lang/{lang}.js");

                if (locFile == null)
                {
                    return name;
                }
                
                _lang = ParseJson(locFile.VirtualPath);
            }

            return _lang.Get(name) ?? name;
        }

        private string GetSetting(string name)
        {
            if (_roxySettings == null)
            {
                _roxySettings = ParseJson(ConfigFilePath);
            }

            if (_roxySettings.TryGetValue(name, out var result))
            {
                return result;
            }

            return result.EmptyNull();
        }

        private bool IsAllowedFileType(string extension)
        {
            extension = extension.EmptyNull().ToLower().TrimEnd('.');

            var setting = GetSetting("FORBIDDEN_UPLOADS").EmptyNull().Trim().ToLower();
            if (setting.HasValue())
            {
                var tmp = Regex.Split(setting, "\\s+");
                if (tmp.Contains(extension))
                    return false;
            }

            setting = GetSetting("ALLOWED_UPLOADS").EmptyNull().Trim().ToLower();
            if (setting.HasValue())
            {
                var tmp = Regex.Split(setting, "\\s+");
                if (!tmp.Contains(extension))
                    return false;
            }

            return true;
        }

        private bool IsAjaxUpload(string method = null)
        {
            return method == "ajax" || Request.IsAjaxRequest();
            
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private string GetMimeType(IFile file)
        {
            return (file as MediaFileInfo)?.MimeType ?? MimeTypes.MapNameToMimeType(file.Name);
        }

        internal class RoxyFolder
        {
            public IDirectory Folder { get; set; }
            public string DisplayName { get; set; }
            public int SubFolders { get; set; }
        }

        #endregion
    }
}
