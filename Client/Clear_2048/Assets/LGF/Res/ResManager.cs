using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LGF.Path;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace LGF.Res
{
    public class ResManager : MonoSingleton<ResManager>
    {
        /// <summary>
        /// 资源服务器地址
        /// </summary>
        public string HostServerUrl = ""; // TODO: 觉得有用，预留以后更改

        private string m_ApplicableGameVersion;
        private int m_InternalResourceVersion;

        /// <summary>
        /// 获取当前资源适用的游戏版本号。
        /// </summary>
        public string ApplicableGameVersion => m_ApplicableGameVersion;

        /// <summary>
        /// 获取当前内部资源版本号。
        /// </summary>
        public int InternalResourceVersion => m_InternalResourceVersion;

        /// <summary>
        /// 默认资源包。
        /// </summary>
        internal ResourcePackage DefaultPackage { get; private set; }

        /// <summary>
        ///  资源包 
        /// </summary>
        private Dictionary<string, ResourcePackage> m_PackageDic { get; } = new();

        /// <summary>
        /// 所有资源信息
        /// </summary>
        private readonly Dictionary<string, AssetInfo> _assetInfoDic = new();

        protected override void Awake()
        {
            base.Awake();
            YooAssets.Initialize();
            DefaultPackage = YooAssets.CreatePackage("DefaultPackage");
            YooAssets.SetDefaultPackage(DefaultPackage);
            StartCoroutine(InitializeYooAsset());
        }

        private IEnumerator InitializeYooAsset()
        {
#if UNITY_EDITOR
            var initParameters = new EditorSimulateModeParameters();
            var simulateManifestFilePath =
                EditorSimulateModeHelper.SimulateBuild(EDefaultBuildPipeline.BuiltinBuildPipeline, "DefaultPackage");
            initParameters.SimulateManifestFilePath = simulateManifestFilePath;
            yield return DefaultPackage.InitializeAsync(initParameters);
            Game.EventManager.Trigger(GameEvent.YooAssetInitialized);
#else
            // 注意：GameQueryServices.cs 太空战机的脚本类，详细见StreamingAssetsHelper.cs
            string defaultHostServer = GetHostServerURL();
            string fallbackHostServer = GetHostServerURL();
            Debug.Log($"{defaultHostServer}");
            var initParameters = new HostPlayModeParameters();

            initParameters.DecryptionServices = new FileOffsetDecryption();
            initParameters.BuildinQueryServices = new GameQueryServices();
            initParameters.RemoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);

            var initOperation = DefaultPackage.InitializeAsync(initParameters);
            yield return initOperation;

            if (initOperation.Status == EOperationStatus.Succeed)
            {
                Debug.Log("资源包初始化成功！");
                StartCoroutine(UpdatePackageVersion());
            }
            else
            {
                Debug.LogError($"资源包初始化失败：{initOperation.Error}");
            }
# endif
        }


        private string packageVersion;

        private IEnumerator UpdatePackageVersion()
        {
            bool savePackageVersion = false;
            var operation = DefaultPackage.UpdatePackageVersionAsync(savePackageVersion);
            yield return operation;

            if (operation.Status == EOperationStatus.Succeed)
            {
                //更新成功
                packageVersion = operation.PackageVersion;
                yield return StartCoroutine(UpdatePackageManifest());

                Game.EventManager.Trigger(GameEvent.YooAssetInitialized);
                Debug.Log($"Updated package Version : {packageVersion}");
            }
            else
            {
                //更新失败
                Debug.LogError(operation.Error);
            }
        }

        private IEnumerator UpdatePackageManifest()
        {
            // 更新成功后自动保存版本号，作为下次初始化的版本。
            // 也可以通过operation.SavePackageVersion()方法保存。
            bool savePackageVersion = true;
            var package = YooAssets.GetPackage("DefaultPackage");
            var operation = package.UpdatePackageManifestAsync(packageVersion, savePackageVersion);
            yield return operation;

            if (operation.Status == EOperationStatus.Succeed)
            {
                Debug.Log("开始下载");
                StartCoroutine(Download());
                //更新成功
            }
            else
            {
                //更新失败
                Debug.LogError(operation.Error);
            }
        }

        IEnumerator Download()
        {
            int downloadingMaxNum = 10;
            int failedTryAgain = 3;
            var package = YooAssets.GetPackage("DefaultPackage");
            var downloader = package.CreateResourceDownloader(downloadingMaxNum, failedTryAgain);

            //没有需要下载的资源
            if (downloader.TotalDownloadCount == 0)
            {
                yield break;
            }

            //需要下载的文件总数和总大小
            int totalDownloadCount = downloader.TotalDownloadCount;
            long totalDownloadBytes = downloader.TotalDownloadBytes;

            //注册回调方法
            downloader.OnDownloadErrorCallback = OnDownloadErrorFunction;
            downloader.OnDownloadProgressCallback = OnDownloadProgressUpdateFunction;
            downloader.OnDownloadOverCallback = OnDownloadOverFunction;
            downloader.OnStartDownloadFileCallback = OnStartDownloadFileFunction;

            //开启下载
            downloader.BeginDownload();
            yield return downloader;

            //检测下载结果
            if (downloader.Status == EOperationStatus.Succeed)
            {
                Debug.Log("全部下载成功");
                //下载成功
            }
            else
            {
                //下载失败
            }
        }

        // 开始下载回调 
        private void OnStartDownloadFileFunction(string filename, long sizebytes)
        {
            Debug.Log("开始下载");
        }

        private void OnDownloadOverFunction(bool issucceed)
        {
            if (issucceed)
            {
                Debug.Log("下载成功");
            }
            else
            {
                Debug.Log("下载失败");
            }
        }

        private void OnDownloadProgressUpdateFunction(int totaldownloadcount, int currentdownloadcount,
            long totaldownloadbytes, long currentdownloadbytes)
        {
            Debug.Log($"下载进度：{currentdownloadcount}/{totaldownloadcount}，{currentdownloadbytes}/{totaldownloadbytes}");
        }

        private void OnDownloadErrorFunction(string filename, string error)
        {
            Debug.LogError($"下载失败：{filename}，{error}");
        }


        private string GetHostServerURL()
        {
            //string hostServerIP = "http://10.0.2.2"; //安卓模拟器地址
            string hostServerIP = "http://192.168.31.41:8080/";
            string appVersion = "v1.0";

#if UNITY_EDITOR
            if (UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.Android)
                return $"{hostServerIP}/CDN/Android/{appVersion}";
            else if (UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.iOS)
                return $"{hostServerIP}/CDN/IPhone/{appVersion}";
            else if (UnityEditor.EditorUserBuildSettings.activeBuildTarget == UnityEditor.BuildTarget.WebGL)
                return $"{hostServerIP}/CDN/WebGL/{appVersion}";
            else
                return $"{hostServerIP}/CDN/PC/{appVersion}";
#else
        if (Application.platform == RuntimePlatform.Android)
            return $"{hostServerIP}/CDN/Android/{appVersion}";
        else if (Application.platform == RuntimePlatform.IPhonePlayer)
            return $"{hostServerIP}/CDN/IPhone/{appVersion}";
        else if (Application.platform == RuntimePlatform.WebGLPlayer)
            return $"{hostServerIP}/CDN/WebGL/{appVersion}";
        else
            return $"{hostServerIP}/CDN/PC/{appVersion}";
#endif
        }

        #region 获取资源信息

        /// <summary>
        /// 获取资源包
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="packageName">资源包名</param>
        /// <returns></returns>
        /// <exception cref="Exception">资源信息</exception>
        public AssetInfo GetAssetInfo(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new Exception($"{nameof(location)} is null or empty !");
            }

            if (string.IsNullOrEmpty(packageName))
            {
                if (_assetInfoDic.TryGetValue(location, out AssetInfo assetInfo))
                    return assetInfo; // 从缓存中获取

                // 从默认资源包中获取
                assetInfo = YooAssets.GetAssetInfo(location);
                _assetInfoDic[location] = assetInfo;
                return assetInfo;
            }
            else
            {
                string key = $"{packageName}/{location}";
                if (_assetInfoDic.TryGetValue(key, out AssetInfo assetInfo))
                    return assetInfo;

                var package = YooAssets.GetPackage(packageName);
                if (package == null)
                    throw new Exception($"The package does not exist. Package Name :{packageName}");

                assetInfo = package.GetAssetInfo(location);
                _assetInfoDic[key] = assetInfo;
                return assetInfo;
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tag">资源标签。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string tag, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tag);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tag);
            }
        }

        /// <summary>
        /// 获取资源信息列表。
        /// </summary>
        /// <param name="tags">资源标签列表。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>资源信息列表。</returns>
        public AssetInfo[] GetAssetInfos(string[] tags, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.GetAssetInfos(tags);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.GetAssetInfos(tags);
            }
        }


        /// <summary>
        /// 检查资源定位地址是否有效。
        /// </summary>
        /// <param name="location">资源的定位地址</param>
        /// <param name="packageName">资源包名称。</param>
        public bool CheckLocationValid(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.CheckLocationValid(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.CheckLocationValid(location);
            }
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="location">资源的定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(location);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.IsNeedDownloadFromRemote(location);
            }
        }

        /// <summary>
        /// 是否需要从远端更新下载。
        /// </summary>
        /// <param name="assetInfo">资源信息。</param>
        /// <param name="packageName">资源包名称。</param>
        public bool IsNeedDownloadFromRemote(AssetInfo assetInfo, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.IsNeedDownloadFromRemote(assetInfo);
            }
            else
            {
                var package = YooAssets.GetPackage(packageName);
                return package.IsNeedDownloadFromRemote(assetInfo);
            }
        }

        /// <summary>
        ///  检测资源包是否存在
        /// </summary>
        /// <param name="location">资源地址</param>
        /// <param name="packageName">资源包名</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public CheckAssetResult CheckAssetExists(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                throw new Exception($"{nameof(packageName)} is null or empty !");
            }

            AssetInfo assetInfo = GetAssetInfo(location, packageName);
            if (!CheckLocationValid(location, packageName))
            {
                return CheckAssetResult.NotExist;
            }

            if (assetInfo == null)
            {
                return CheckAssetResult.NotExist;
            }

            if (IsNeedDownloadFromRemote(location, packageName))
            {
                return CheckAssetResult.AssetOnline;
            }

            return CheckAssetResult.AssetOnDisk;
        }

        /// <summary>
        ///  获取资源包key
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="packageName">资源包名</param>
        /// <returns>key</returns>
        private string GetCharacterKey(string location, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return location;
            }

            return $"{packageName}/{location}";
        }

        #endregion


        #region 获取资源句柄

        /// <summary>
        /// 获取同步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleSync<T>(string location, string packageName = "")
        {
            return GetHandleSync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleSync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetSync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetSync(location, assetType);
        }

        /// <summary>
        /// 获取异步资源句柄。
        /// </summary>
        /// <param name="location">资源定位地址。</param>
        /// <param name="packageName">指定资源包的名称。不传使用默认资源包</param>
        /// <typeparam name="T">资源类型。</typeparam>
        /// <returns>资源句柄。</returns>
        private AssetHandle GetHandleAsync<T>(string location, string packageName = "") where T : UnityEngine.Object
        {
            return GetHandleAsync(location, typeof(T), packageName);
        }

        private AssetHandle GetHandleAsync(string location, Type assetType, string packageName = "")
        {
            if (string.IsNullOrEmpty(packageName))
            {
                return YooAssets.LoadAssetAsync(location, assetType);
            }

            var package = YooAssets.GetPackage(packageName);
            return package.LoadAssetAsync(location, assetType);
        }

        #endregion

        /**
         * 加载资源
         * @param location 资源定位地址
         * @param packageName 指定资源包的名称。不传使用默认资源包
         */
        public T LoadAsset<T>(string location, string packageName = "") where T : Object
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new Exception("location is null or empty !");
            }

            // string assetObjectKey = GetCharacterKey(location, packageName);
            // TODO: 从缓存中获取
            AssetHandle handle = GetHandleSync<T>(location, packageName);
            T ret = handle.AssetObject as T;
            // TODO: 缓存资源
            return ret;
        }

        public Sprite LoadSprite(string pathName, string assName, string packageName = "")
        {
            if (string.IsNullOrEmpty(assName))
            {
                throw new Exception("location is null or empty !");
            }

            string path = PathConfig.GetAtlasPath(pathName, assName);
            // TODO 从缓存获取
            var sprite = LoadAsset<Sprite>(path, packageName);

            return sprite;
        }

        public GameObject LoadGameObject(string location, Transform parent = null, string packageName = "")
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new Exception("location is null or empty !");
            }

            string assetObjectKey = GetCharacterKey(location, packageName);
            // TODO 从缓存获取
            AssetHandle handle = GetHandleSync<GameObject>(location, packageName);

            GameObject obj = AssetsReference.Instantiate(handle.AssetObject as GameObject, parent, this).gameObject;

            return obj;
        }

        public async Task<GameObject> LoadGameObjectAsync(string location, string packageName = "",
            Transform parent = null)
        {
            if (string.IsNullOrEmpty(location))
            {
                throw new Exception("location is null or empty !");
            }

            string assetObjectKey = GetCharacterKey(location, packageName);
            // TODO 从缓存获取
            AssetHandle handle = GetHandleAsync<GameObject>(location, packageName);

            handle.WaitForAsyncComplete();

            GameObject obj = AssetsReference.Instantiate(handle.AssetObject as GameObject, parent, this)
                .gameObject;

            return obj;
        }

        public void UnloadAsset(object obj)
        {
            if (obj == null) return;
            // DefaultPackage.TryUnloadUnusedAsset();
        }

        private void OnDestroy()
        {
            YooAssets.Destroy();
        }


        /// <summary>
        /// 远端资源地址查询服务类
        /// </summary>
        private class RemoteServices : IRemoteServices
        {
            private readonly string _defaultHostServer;
            private readonly string _fallbackHostServer;

            public RemoteServices(string defaultHostServer, string fallbackHostServer)
            {
                _defaultHostServer = defaultHostServer;
                _fallbackHostServer = fallbackHostServer;
            }

            string IRemoteServices.GetRemoteMainURL(string fileName)
            {
                return $"{_defaultHostServer}/{fileName}";
            }

            string IRemoteServices.GetRemoteFallbackURL(string fileName)
            {
                return $"{_fallbackHostServer}/{fileName}";
            }
        }

        private class FileOffsetDecryption : IDecryptionServices
        {
            /// <summary>
            /// 同步方式获取解密的资源包对象
            /// 注意：加载流对象在资源包对象释放的时候会自动释放
            /// </summary>
            AssetBundle IDecryptionServices.LoadAssetBundle(DecryptFileInfo fileInfo, out Stream managedStream)
            {
                managedStream = null;
                return AssetBundle.LoadFromFile(fileInfo.FileLoadPath, fileInfo.ConentCRC, GetFileOffset());
            }

            /// <summary>
            /// 异步方式获取解密的资源包对象
            /// 注意：加载流对象在资源包对象释放的时候会自动释放
            /// </summary>
            AssetBundleCreateRequest IDecryptionServices.LoadAssetBundleAsync(DecryptFileInfo fileInfo,
                out Stream managedStream)
            {
                managedStream = null;
                return AssetBundle.LoadFromFileAsync(fileInfo.FileLoadPath, fileInfo.ConentCRC, GetFileOffset());
            }

            private static ulong GetFileOffset()
            {
                return 32;
            }
        }
    }
}

public class GameQueryServices : IBuildinQueryServices
{
    /// <summary>
    /// 查询内置文件的时候，是否比对文件哈希值
    /// </summary>
    public static bool CompareFileCRC = false;

    public bool Query(string packageName, string fileName, string fileCRC)
    {
        // 注意：fileName包含文件格式
        return StreamingAssetsHelper.FileExists(packageName, fileName, fileCRC);
    }
}

#if UNITY_EDITOR
public sealed class StreamingAssetsHelper
{
    public static void Init()
    {
    }

    public static bool FileExists(string packageName, string fileName, string fileCRC)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, StreamingAssetsDefine.RootFolderName,
            packageName, fileName);
        if (File.Exists(filePath))
        {
            if (GameQueryServices.CompareFileCRC)
            {
                // TODO: YooAsset.Editor 中封装被去除后替代方法
                string crc32 = HashUtility.FileCRC32(filePath);
                return crc32 == fileCRC;
            }
            else
            {
                return true;
            }
        }
        else
        {
            return false;
        }
    }
}
#else
public sealed class StreamingAssetsHelper
{
    private class PackageQuery
    {
        public readonly Dictionary<string, BuildinFileManifest.Element> Elements =
            new Dictionary<string, BuildinFileManifest.Element>(1000);
    }

    private static bool _isInit = false;
    private static readonly Dictionary<string, PackageQuery> _packages = new Dictionary<string, PackageQuery>(10);

    /// <summary>
    /// 初始化
    /// </summary>
    public static void Init()
    {
        if (_isInit == false)
        {
            _isInit = true;

            var manifest = Resources.Load<BuildinFileManifest>("BuildinFileManifest");
            if (manifest != null)
            {
                foreach (var element in manifest.BuildinFiles)
                {
                    if (_packages.TryGetValue(element.PackageName, out PackageQuery package) == false)
                    {
                        package = new PackageQuery();
                        _packages.Add(element.PackageName, package);
                    }

                    package.Elements.Add(element.FileName, element);
                }
            }
        }
    }

    /// <summary>
    /// 内置文件查询方法
    /// </summary>
    public static bool FileExists(string packageName, string fileName, string fileCRC32)
    {
        if (_isInit == false)
            Init();

        if (_packages.TryGetValue(packageName, out PackageQuery package) == false)
            return false;

        if (package.Elements.TryGetValue(fileName, out var element) == false)
            return false;

        if (GameQueryServices.CompareFileCRC)
        {
            return element.FileCRC32 == fileCRC32;
        }
        else
        {
            return true;
        }
    }
}

#endif

public class StreamingAssetsDefine
{
    /// <summary>
    /// 根目录名称（保持和YooAssets资源系统一致）
    /// </summary>
    public const string RootFolderName = "Clear_2048";
}

/// <summary>
/// 内置资源清单
/// </summary>
public class BuildinFileManifest : ScriptableObject
{
    [Serializable]
    public class Element
    {
        public string PackageName;
        public string FileName;
        public string FileCRC32;
    }

    public List<Element> BuildinFiles = new List<Element>();
}