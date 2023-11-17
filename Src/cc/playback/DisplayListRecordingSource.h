#ifndef DisplayListRecordingSource_h
#define DisplayListRecordingSource_h

namespace cc {

class RecordingSourceContainer {
public:
    SkPictureRecorder* recorder;
    IntRect area;
    bool dirty;

    RecordingSourceContainer(const IntRect& area)
        : recorder(0)
        , area(area)
        , dirty(true)
    {}

    ~RecordingSourceContainer();
};

// ���ϰ��PicturePileһ����˼
class DisplayListRecordingSource {

public:
    DisplayListRecordingSource();

    // Used by WebViewCore
    void invalidate(const IntRect& dirtyRect);
    void applyDirtyRects();
    void setSize(const IntSize& size);

    void updateRecordingSourcesIfNeeded(WebLayerImplClient* client, canDrawRect);
    void updateRecordingSources(WebLayerImplClient* client);

private:
    Vector<IntRect> m_dirtyRects;
    blink::IntSize m_size; // һ�����Ļ��㣬����ȫ��layer size��һ���������ɸ�tile�����size
    Vector<RecordingSourceContainer> m_recordingSourcePile;
};

}

#endif // DisplayListRecordingSource_h