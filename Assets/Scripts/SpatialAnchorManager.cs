using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MagicLeapSupport;
using UnityEngine.XR.OpenXR.NativeTypes;

public class SpatialAnchorManager : MonoBehaviour
{
    [SerializeField]
    public InputAction menuInputAction = new InputAction(binding: "<XRController>/menuButton", expectedControlType: "Button");
    [SerializeField]
    public InputAction bumperInputAction = new InputAction(binding: "<XRController>/gripButton", expectedControlType: "Button");
    
    [SerializeField] private Transform controllerTransform;
    [SerializeField] private ARAnchorManager anchorManager;
    
    private MagicLeapSpatialAnchorsFeature spatialAnchorsFeature;
    private MagicLeapLocalizationMapFeature localizationMapFeature;
    private MagicLeapSpatialAnchorsStorageFeature storageFeature;

    private struct PublishedAnchor
    {
        public ulong AnchorId;
        public string AnchorMapPositionId;
        public ARAnchor AnchorObject;
    }

    private List<PublishedAnchor> publishedAnchors = new();
    private List<ARAnchor> activeAnchors = new();
    private List<ARAnchor> pendingPublishedAnchors = new();
    private List<ARAnchor> localAnchors = new();
    private MagicLeapLocalizationMapFeature.LocalizationEventData mapData;
    private MLXrAnchorSubsystem activeSubsystem;

    private IEnumerator Start()
    {
        yield return new WaitUntil(AreSubsystemsLoaded);

        spatialAnchorsFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsFeature>();
        storageFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsStorageFeature>();
        localizationMapFeature = OpenXRSettings.Instance.GetFeature<MagicLeapLocalizationMapFeature>();
        if (!spatialAnchorsFeature || !localizationMapFeature || !storageFeature)
        {
            enabled = false;
        }

        storageFeature.OnCreationCompleteFromStorage += OnCreateFromStorageComplete;
        storageFeature.OnPublishComplete += OnPublishComplete;
        storageFeature.OnQueryComplete += OnQueryComplete;
        storageFeature.OnDeletedComplete += OnDeletedComplete;
        
        menuInputAction.Enable();
        bumperInputAction.Enable();
        menuInputAction.performed += OnMenu;
        bumperInputAction.performed += OnBumper;
        
        localizationMapFeature.EnableLocalizationEvents(true);
        LocalizeMap();
        
        QueryAnchors();
    }

    private bool AreSubsystemsLoaded()
    {
        if (XRGeneralSettings.Instance == null) return false;
        if (XRGeneralSettings.Instance.Manager == null) return false;
        var activeLoader = XRGeneralSettings.Instance.Manager.activeLoader;
        if (activeLoader == null) return false;
        activeSubsystem = activeLoader.GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
        return activeSubsystem != null;
    }

    private void OnPublishComplete(ulong anchorId, string anchorMapPositionId)
    {
        for (int i = activeAnchors.Count - 1; i >= 0; i--)
        {
            if (activeSubsystem.GetAnchorId(activeAnchors[i]) == anchorId)
            {
                PublishedAnchor newPublishedAnchor;
                newPublishedAnchor.AnchorId = anchorId;
                newPublishedAnchor.AnchorMapPositionId = anchorMapPositionId;
                newPublishedAnchor.AnchorObject = activeAnchors[i];

                publishedAnchors.Add(newPublishedAnchor);
                activeAnchors.RemoveAt(i);
                break;
            }
        }
    }

    private void OnBumper(InputAction.CallbackContext _)
    {
        Pose currentPose = new Pose(controllerTransform.position, controllerTransform.rotation);
        GameObject newAnchor = Instantiate(anchorManager.anchorPrefab, currentPose.position, currentPose.rotation);
        ARAnchor newAnchorComponent = newAnchor.AddComponent<ARAnchor>();

        activeAnchors.Add(newAnchorComponent);
        localAnchors.Add(newAnchorComponent);
        
        PublishAnchors();
    }

    private void OnMenu(InputAction.CallbackContext _)
    {
        // delete most recent local anchor first
        if (localAnchors.Count > 0)
        {
            Destroy(localAnchors[localAnchors.Count - 1].gameObject);
            localAnchors.RemoveAt(localAnchors.Count - 1);
        }
        //Deleting the last published anchor.
        else if (publishedAnchors.Count > 0)
        {
            storageFeature.DeleteStoredSpatialAnchor(new List<string> { publishedAnchors[publishedAnchors.Count - 1].AnchorMapPositionId });
        }
    }

    private void OnQueryComplete(List<string> anchorMapPositionIds)
    {
        if (publishedAnchors.Count == 0)
        {
            if (!storageFeature.CreateSpatialAnchorsFromStorage(anchorMapPositionIds))
                Debug.LogError("Couldn't create spatial anchors from storage");
            return;
        }

        foreach (string anchorMapPositionId in anchorMapPositionIds)
        {
            var matches = publishedAnchors.Where(p => p.AnchorMapPositionId == anchorMapPositionId);
            if (matches.Count() == 0)
            {
                if (!storageFeature.CreateSpatialAnchorsFromStorage(new List<string>() { anchorMapPositionId }))
                    Debug.LogError("Couldn't create spatial anchors from storage");
            }
        }

        for (int i = publishedAnchors.Count - 1; i >= 0; i--)
        {
            if (!anchorMapPositionIds.Contains(publishedAnchors[i].AnchorMapPositionId))
            {
                Destroy(publishedAnchors[i].AnchorObject.gameObject);
                publishedAnchors.RemoveAt(i);
            }
        }

    }

    private void OnDeletedComplete(List<string> anchorMapPositionIds)
    {
        foreach (string anchorMapPositionId in anchorMapPositionIds)
        {
            for (int i = publishedAnchors.Count - 1; i >= 0; i--)
            {
                if (publishedAnchors[i].AnchorMapPositionId == anchorMapPositionId)
                {
                    Destroy(publishedAnchors[i].AnchorObject.gameObject);
                    publishedAnchors.RemoveAt(i);
                    break;
                }
            }
        }
    }

    private void OnCreateFromStorageComplete(Pose pose, ulong anchorId, string anchorMapPositionId, XrResult result)
    {
        if (result != XrResult.Success)
        {
            Debug.LogError("Could not create anchor from storage: " + result);
            return;
        }

        PublishedAnchor newPublishedAnchor;
        newPublishedAnchor.AnchorId = anchorId;
        newPublishedAnchor.AnchorMapPositionId = anchorMapPositionId;

        GameObject newAnchor = Instantiate(anchorManager.anchorPrefab, pose.position, pose.rotation);

        ARAnchor newAnchorComponent = newAnchor.AddComponent<ARAnchor>();

        newPublishedAnchor.AnchorObject = newAnchorComponent;

        publishedAnchors.Add(newPublishedAnchor);
    }

    public void PublishAnchors()
    {
        if (localizationMapFeature != null)
        {
            localizationMapFeature.GetLatestLocalizationMapData(out mapData);
            if (mapData.State != MagicLeapLocalizationMapFeature.LocalizationMapState.Localized)
                return;
        }
        else return;
        
        foreach (ARAnchor anchor in localAnchors)
            pendingPublishedAnchors.Add(anchor);

        localAnchors.Clear();
    }

    public void LocalizeMap()
    {
        if (localizationMapFeature == null)
            return;

        string map = "1195c3c3-fbc7-7018-9754-68f7fd5c4aed";
        var res = localizationMapFeature.RequestMapLocalization(map);
        if (res != XrResult.Success)
        {
            Debug.LogError("Failed to request localization: " + res);
            return;
        }

        //On map change, we need to clear up present published anchors and query new ones
        foreach (PublishedAnchor obj in publishedAnchors)
            Destroy(obj.AnchorObject.gameObject);
        publishedAnchors.Clear();

        foreach (ARAnchor anchor in localAnchors)
            Destroy(anchor.gameObject);
        localAnchors.Clear();

        activeAnchors.Clear();
    }

    public void QueryAnchors()
    {
        if (!storageFeature.QueryStoredSpatialAnchors(controllerTransform.position, 10f))
        {
            Debug.LogError("Could not query stored anchors");
        }
    }

    void Update()
    {
        if (pendingPublishedAnchors.Count > 0)
        {
            for (int i = pendingPublishedAnchors.Count - 1; i >= 0; i--)
            {
                if (pendingPublishedAnchors[i].trackingState == TrackingState.Tracking)
                {
                    ulong anchorId = activeSubsystem.GetAnchorId(pendingPublishedAnchors[i]);
                    if (!storageFeature.PublishSpatialAnchorsToStorage(new List<ulong>() { anchorId }, 0))
                    {
                        Debug.LogError($"Failed to publish anchor {anchorId} at position {pendingPublishedAnchors[i].gameObject.transform.position} to storage");
                    }
                    else
                    {
                        pendingPublishedAnchors.RemoveAt(i);
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        menuInputAction.performed -= OnMenu;
        bumperInputAction.performed -= OnBumper;
        
        menuInputAction.Dispose();
        bumperInputAction.Dispose();

        if (localizationMapFeature != null)
        {
            localizationMapFeature.EnableLocalizationEvents(false);
        }

        storageFeature.OnCreationCompleteFromStorage -= OnCreateFromStorageComplete;
        storageFeature.OnPublishComplete -= OnPublishComplete;
        storageFeature.OnQueryComplete -= OnQueryComplete;
        storageFeature.OnDeletedComplete -= OnDeletedComplete;
    }
}