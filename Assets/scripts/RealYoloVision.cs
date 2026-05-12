using UnityEngine;
using System.Collections.Generic;

using System.Linq;

[System.Serializable]
public class YoloDetection
{
    public Rect boundingBox;     
    public float confidence;
    public int classId;
    public string className;
    public Vector3 worldPosition;
    public Transform trackedTarget;
}

public class RealYoloVision : MonoBehaviour
{
    public Camera inferenceCamera;
    public Unity.InferenceEngine.ModelAsset modelAsset; 
    
    public int imageWidth = 640;
    public int imageHeight = 640;
    public float confThreshold = 0.5f;
    public float iouThreshold = 0.45f;
    public int maxDetections = 10;
    
    public string targetTag = "Enemy";
    public LayerMask occlusionMask;
    public float updateRate = 15f; 
    
    private Unity.InferenceEngine.Model runtimeModel;
    private Unity.InferenceEngine.Worker worker;
    private Unity.InferenceEngine.Tensor<float> inputTensor;
    private RenderTexture captureTexture;
    
    private List<YoloDetection> currentDetections = new List<YoloDetection>();
    private bool isProcessing;
    
    public List<YoloDetection> GetDetections() => currentDetections;
    
    void Start()
    {
        InitializeSentis();
        SetupCaptureResources();
        StartCoroutine(InferenceLoop());
    }
    
    void InitializeSentis()
    {
        runtimeModel = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
        worker = new Unity.InferenceEngine.Worker(runtimeModel, Unity.InferenceEngine.BackendType.GPUCompute);
    }
    
    void SetupCaptureResources()
    {
        captureTexture = new RenderTexture(imageWidth, imageHeight, 24);
        captureTexture.Create();
        
        if (inferenceCamera.targetTexture != null)
            inferenceCamera.targetTexture.Release();
            
        inferenceCamera.targetTexture = captureTexture;
    }
    
    System.Collections.IEnumerator InferenceLoop()
    {
        WaitForSeconds wait = new WaitForSeconds(1f / updateRate);
        
        while (true)
        {
            yield return wait;
            
            if (!isProcessing && inferenceCamera != null)
            {
                isProcessing = true;
                PerformInference();
                isProcessing = false;
            }
        }
    }
    
    void PerformInference()
    {
        inputTensor = Unity.InferenceEngine.TextureConverter.ToTensor(captureTexture, imageWidth, imageHeight, 3);
        
        worker.Schedule(inputTensor);
        
        Unity.InferenceEngine.Tensor<float> outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
        
        // �������� ������ � ������ (�� ��� ������� ������)
        List<YoloDetection> detections = ParseYOLOOutput(outputTensor);
        
        detections = ApplyNMS(detections);
        ProjectTo3D(detections);
        
        currentDetections = detections;
        
        inputTensor.Dispose();
    }
    
    List<YoloDetection> ParseYOLOOutput(Unity.InferenceEngine.Tensor<float> output)
    {
        var detections = new List<YoloDetection>();
        
        int numClasses = output.shape[1] - 4; 
        int numElements = output.shape[2]; 
        
        // ������� �������� ������� � GPU � ������� ������ C#
        float[] outputArray = output.DownloadToArray();
        
        for (int i = 0; i < numElements; i++)
        {
            float maxConf = 0;
            int bestClass = -1;
            
            for (int c = 0; c < numClasses; c++)
            {
                // ��������� ������ � ������� ������� (������ ���������� 3D-�����������)
                int index = (4 + c) * numElements + i;
                float score = outputArray[index];
                
                if (score > maxConf)
                {
                    maxConf = score;
                    bestClass = c;
                }
            }
            
            if (maxConf > confThreshold)
            {
                // ������ ���������� Bounding Box
                float x = outputArray[0 * numElements + i] / imageWidth;
                float y = outputArray[1 * numElements + i] / imageHeight;
                float w = outputArray[2 * numElements + i] / imageWidth;
                float h = outputArray[3 * numElements + i] / imageHeight;
                
                float x1 = x - w / 2;
                float y1 = y - h / 2;
                
                var detection = new YoloDetection
                {
                    boundingBox = new Rect(x1, y1, w, h),
                    confidence = maxConf,
                    classId = bestClass,
                    className = GetClassName(bestClass)
                };
                
                detections.Add(detection);
            }
        }
        
        return detections;
    }
    
    List<YoloDetection> ApplyNMS(List<YoloDetection> detections)
    {
        if (detections.Count == 0) return detections;
        
        var sorted = detections.OrderByDescending(d => d.confidence).ToList();
        var results = new List<YoloDetection>();
        var suppressed = new bool[detections.Count];
        
        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i]) continue;
            
            results.Add(sorted[i]);
            
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j]) continue;
                
                float iou = CalculateIoU(sorted[i].boundingBox, sorted[j].boundingBox);
                
                if (iou > iouThreshold)
                {
                    suppressed[j] = true;
                }
            }
        }
        
        return results.Take(maxDetections).ToList();
    }
    
    float CalculateIoU(Rect a, Rect b)
    {
        float x1 = Mathf.Max(a.x, b.x);
        float y1 = Mathf.Max(a.y, b.y);
        float x2 = Mathf.Min(a.x + a.width, b.x + b.width);
        float y2 = Mathf.Min(a.y + a.height, b.y + b.height);
        
        float intersection = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
        float union = a.width * a.height + b.width * b.height - intersection;
        
        return intersection / union;
    }
    
    void ProjectTo3D(List<YoloDetection> detections)
    {
        foreach (var det in detections)
        {
            float viewX = det.boundingBox.x + det.boundingBox.width / 2;
            float viewY = 1f - (det.boundingBox.y + det.boundingBox.height / 2);
            Vector2 centerViewport = new Vector2(viewX, viewY);
            
            Ray ray = inferenceCamera.ViewportPointToRay(centerViewport);
            
            if (Physics.Raycast(ray, out RaycastHit hit, inferenceCamera.farClipPlane))
            {
                Vector3 dirToHit = (hit.point - inferenceCamera.transform.position).normalized;
                if (!Physics.Raycast(inferenceCamera.transform.position, dirToHit, hit.distance, occlusionMask))
                {
                    det.worldPosition = hit.point;
                    
                    if (hit.collider.CompareTag(targetTag))
                    {
                        det.trackedTarget = hit.collider.transform;
                    }
                    else
                    {
                        Collider[] colliders = Physics.OverlapSphere(hit.point, 1.5f);
                        foreach (var col in colliders)
                        {
                            if (col.CompareTag(targetTag))
                            {
                                det.trackedTarget = col.transform;
                                break;
                            }
                        }
                    }
                }
            }
        }
    }
    
    string GetClassName(int classId)
    {
        return classId == 0 ? "person" : $"Class_{classId}";
    }
    
    void OnDestroy()
    {
        worker?.Dispose();
        if (captureTexture != null) captureTexture.Release();
    }
}