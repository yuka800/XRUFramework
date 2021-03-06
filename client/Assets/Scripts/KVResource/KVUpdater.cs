﻿using System;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;


public class KVUpdater : MonoBehaviour
{
    enum EUpdateState
    {
        None = 0,
        CheckUpdateCatalogs,
        AfterCheckCatalogs,
        StartUpdateCatalogs,
        CheckUpdateData,
        StartUpdateData,
        UpdateComplete,
    }

    private EUpdateState curState = EUpdateState.None;
    private List<string> updateCatalogsList;
    private AsyncOperationHandle<List<string>> checkHandle;
    private AsyncOperationHandle<List<IResourceLocator>> catelogHandle;
    private AsyncOperationHandle downloadHandle;
    private float checkUpdateTime = 0f;
    private float CHECKTIMEMAX = 5f;

    private Text text_info;
    private Text text_progress;
    private Image slider;
    
    List<string> infoList = new List<string>();

    void Awake()
    {
        text_info = transform.Find("text_info").GetComponent<Text>();
        text_progress = transform.Find("slider/progress").GetComponent<Text>();
        slider = transform.Find("slider/top").GetComponent<Image>();
        slider.gameObject.SetActive(false);

        MarkNeedDownloadState(true);
    }

    void Start ()
    {
        infoList.Add($"dataPath:{Application.dataPath}\n");
        infoList.Add($"consoleLogPath:{Application.consoleLogPath}\n");
        infoList.Add($"persistentDataPath:{Application.persistentDataPath}\n");
        infoList.Add($"streamingAssetsPath:{Application.streamingAssetsPath}\n");
        infoList.Add($"temporaryCachePath:{Application.temporaryCachePath}\n");
        infoList.Add($"BuildPath:{Addressables.BuildPath}\n");
        infoList.Add($"PlayerBuildDataPath:{Addressables.PlayerBuildDataPath}\n");
        infoList.Add($"RuntimePath:{Addressables.RuntimePath}\n");
        string info = String.Concat(infoList.ToArray());
        text_info.text = info;
        
        Debug.Log($"Application:\n{info}");
        
        SetState(EUpdateState.None);
        StartCheckUpdate();
    }

    void StartCheckUpdate()
    {
        UpdateProgressText("正在检测资源更新");
        slider.fillAmount = 0f;

        StartCoroutine(CheckUpdate());
    }

    IEnumerator CheckUpdate()
    {
        SetState(EUpdateState.CheckUpdateCatalogs);

        var needUpdateCatalogs = false;
        var start = DateTime.Now;
        //开始连接服务器检查更新
        checkHandle = Addressables.CheckForCatalogUpdates(false);
        //检查结束，验证结果
        checkHandle.Completed += operationHandle =>
        {
            if (checkHandle.Status == AsyncOperationStatus.Succeeded)
            {
                List<string> catalogs = checkHandle.Result;
                if (catalogs != null && catalogs.Count > 0)
                {
                    needUpdateCatalogs = true;
                    updateCatalogsList = catalogs;
                }
            }
            SetState(EUpdateState.AfterCheckCatalogs);
        };   
        yield return checkHandle;
        Debug.Log($"CheckIfNeededUpdate({needUpdateCatalogs}) use {(DateTime.Now - start).Milliseconds} ms");    
        Addressables.Release(checkHandle);
        
        if (needUpdateCatalogs)
        {
            yield return DownloadCatalogs();
        }
        
        yield return StartDownload();
    }

    IEnumerator StartDownload()
    {
        if (IsLastDownloadComplete())
        {
            //检查到有资源需要更新
            UpdateProgressText("有资源需要更新");
            yield return DownloadUpdateData();
        }

        //没有资源需要更新，或者连接服务器失败
        DownComplete();
    }
    
    public void DownComplete(bool isSuccess = true)
    {
        slider.fillAmount = 1;
        UpdateProgressText($"下载完成 state:{curState} isSuccess:{isSuccess}");
    }

    IEnumerator DownloadCatalogs()
    {
        var start = DateTime.Now;
        //开始下载资源
        SetState(EUpdateState.StartUpdateCatalogs);
        catelogHandle = Addressables.UpdateCatalogs(updateCatalogsList, false);
        catelogHandle.Completed += handle =>
        {
            MarkNeedDownloadState(true);
            //下载完成
            SetState(EUpdateState.UpdateComplete);
            Debug.Log($"下载完成Catalogs------------- use time:{(DateTime.Now - start).Milliseconds} ms");
        }; 
        yield return catelogHandle;
        Addressables.Release(catelogHandle);
    }
    
    IEnumerator DownloadUpdateData()
    {
        SetState(EUpdateState.CheckUpdateData);
        List<IResourceLocation> locations = new List<IResourceLocation>();
        foreach (var locator in Addressables.ResourceLocators)
        {
//            Debug.Log($"      locater {locator.LocatorId}");
            foreach (var key in locator.Keys)
            {
                IList<IResourceLocation> locationList;
                if (locator.Locate(key, typeof(object), out locationList))
                {
                    if (locationList.Count > 0)
                    {
                        foreach (var location in locationList)
                        {
//                            Debug.Log($"                                      =========> {location.PrimaryKey}");
                            locations.Add(location);
                        }
                    }
                }
            }
        }

        var start = DateTime.Now;
        var downSize = 0l;
        var sizeAo = Addressables.GetDownloadSizeAsync(locations);
        sizeAo.Completed += handle =>
        {
            downSize = sizeAo.Result;
            Debug.Log($"检查下载资源完成------------- use time:{(DateTime.Now - start).Milliseconds} ms");
            Debug.Log($"  GetDownloadSizeAsync 下载内容大小：{sizeAo.Result}  ");
        };
        yield return sizeAo;
        
        start = DateTime.Now;
        if (downSize > 0)
        {
            SetState(EUpdateState.StartUpdateData);
            slider.gameObject.SetActive(true);
            MarkNeedDownloadState(true);
            downloadHandle = Addressables.DownloadDependenciesAsync(locations);
            downloadHandle.Completed += handle =>
            {
                Debug.Log($"下载DownLoadData------------- use time:{(DateTime.Now - start).Milliseconds} ms");
//                var list = handle.Result as List<IAssetBundleResource>;
//                foreach (var item in list)
//                {
//                    Debug.Log($"------------------ {item.GetAssetBundle().name}");
//                }
            };
            yield return  downloadHandle;
            Addressables.Release(downloadHandle);
        }
        
        {
            MarkNeedDownloadState(false);
        }
        SetState(EUpdateState.UpdateComplete);
        yield break;
    }


	void Update () {
        switch (curState)
        {
            case EUpdateState.CheckUpdateCatalogs:
            {
                checkUpdateTime += Time.deltaTime;
                if (checkUpdateTime > CHECKTIMEMAX)
                {
                    SetState(EUpdateState.AfterCheckCatalogs);
                    StopAllCoroutines();
                    DownComplete(false);
                }
                else if(checkHandle.IsValid())
                {
                    OnDownProgress(checkHandle.PercentComplete);
                }
                break;
            }
            case EUpdateState.StartUpdateCatalogs:
            {
                OnDownProgress(catelogHandle.PercentComplete);
                break;
            }
            case EUpdateState.StartUpdateData:
            {
                if (catelogHandle.IsValid())
                {
                    OnDownProgress(downloadHandle.PercentComplete);
                }
                break;
            }
            default:
                break;
        }
    }
    
    IEnumerator ShowEffect()
    {
        {
            var ao = Addressables.InstantiateAsync("prefabs/cube.prefab");
            ao.Completed += handle =>
            {
                GameObject obj = ao.Result;
                obj.transform.position = Vector3.zero;
                obj.transform.localScale = Vector3.one;
            };
            yield return ao;
        }

        Addressables.ReleaseInstance(gameObject);
//        Addressables.ClearResourceLocators();
        Addressables.ClearDependencyCacheAsync("prefabs/launch.prefab");
        {
            var rootLayer = GameObject.Find("LaunchLayer").GetComponent<Canvas>();
            var loader = KVResourceMgr.Instance.LoadAssetAsync("prefabs/launch.prefab");
            yield return loader;
//            ao.Completed += handle =>
//            {
            GameObject obj = loader.asset as GameObject;
            obj = Instantiate(obj, rootLayer.transform);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
//            };
//            yield return ao;
        }        DestroyImmediate(gameObject);
    }


    void SetState(EUpdateState state)
    {
        Debug.Log($"KVUpdate ====>SetState({state})");
        curState = state;
    }

    void MarkNeedDownloadState(bool needDownload)
    {
        FileStream fileStream = File.Open(GetDownLockFile(), FileMode.Create);
        StreamWriter writer = new StreamWriter(fileStream);
        writer.Write(needDownload);
        writer.Close();
        fileStream.Close();
    }
    
    bool IsLastDownloadComplete()
    {
        FileStream fileStream = File.Open(GetDownLockFile(), FileMode.Open);
        StreamReader reader = new StreamReader(fileStream);
        var result = reader.ReadLine();
        var isDownComplete = ("True".Equals(result));
        reader.Close();
        fileStream.Close();
        return isDownComplete;
    }

    string GetDownLockFile()
    {
        var writePath = Application.persistentDataPath;
        return Path.Combine(writePath, "down.lock");
    }

    void OnDownProgress(float precent)
    {
        slider.fillAmount = precent;
        UpdateProgressText($"状态{curState}:{precent}");
        Debug.Log($"KVUpdate 状态{curState}:{precent}");
    }

    void UpdateProgressText(string msg)
    {
        text_progress.text = msg;
    }
}
