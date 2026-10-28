import AVFoundation
import VideoToolbox

struct EncodedFrame {
    let configuration: Data
    let bytes: Data
    let pts: UInt64
    let key: Bool
}

/// Session methods are confined to the capture queue. VT callbacks may use another queue.
final class H264Encoder {
    private var session: VTCompressionSession?
    private var forceKey = true
    var onFrame: ((EncodedFrame) -> Void)?
    var onError: ((String) -> Void)?

    func start(width: Int32, height: Int32) throws {
        stop()
        let status = VTCompressionSessionCreate(
            allocator: kCFAllocatorDefault, width: width, height: height,
            codecType: kCMVideoCodecType_H264, encoderSpecification: nil,
            imageBufferAttributes: nil, compressedDataAllocator: nil,
            outputCallback: { context, _, status, flags, sample in
                guard let context else { return }
                let encoder = Unmanaged<H264Encoder>.fromOpaque(context).takeUnretainedValue()
                guard status == noErr else { encoder.onError?("VideoToolbox: \(status)"); return }
                guard !flags.contains(.frameDropped), let sample else { return }
                encoder.output(sample)
            }, refcon: Unmanaged.passUnretained(self).toOpaque(), compressionSessionOut: &session)
        try check(status)
        guard let session else { throw EncoderError.status(-1) }
        do {
            try set(kVTCompressionPropertyKey_RealTime, kCFBooleanTrue)
            try set(kVTCompressionPropertyKey_AllowFrameReordering, kCFBooleanFalse)
            try set(kVTCompressionPropertyKey_ProfileLevel, kVTProfileLevel_H264_Main_AutoLevel)
            try set(kVTCompressionPropertyKey_AverageBitRate, NSNumber(value: 4_000_000))
            try set(kVTCompressionPropertyKey_ExpectedFrameRate, NSNumber(value: 30))
            try set(kVTCompressionPropertyKey_MaxKeyFrameInterval, NSNumber(value: 60))
            try set(kVTCompressionPropertyKey_MaxKeyFrameIntervalDuration, NSNumber(value: 2))
            try set(kVTCompressionPropertyKey_ColorPrimaries, kCVImageBufferColorPrimaries_ITU_R_709_2)
            try set(kVTCompressionPropertyKey_TransferFunction, kCVImageBufferTransferFunction_ITU_R_709_2)
            try set(kVTCompressionPropertyKey_YCbCrMatrix, kCVImageBufferYCbCrMatrix_ITU_R_709_2)
            try check(VTCompressionSessionPrepareToEncodeFrames(session))
        } catch { stop(); throw error }
        forceKey = true
    }

    func requestKeyFrame() { forceKey = true }

    func encode(_ sample: CMSampleBuffer) {
        guard let session, let image = CMSampleBufferGetImageBuffer(sample) else { return }
        let properties: CFDictionary? = forceKey ? [kVTEncodeFrameOptionKey_ForceKeyFrame: true] as CFDictionary : nil
        forceKey = false
        let result = VTCompressionSessionEncodeFrame(session, imageBuffer: image,
            presentationTimeStamp: CMSampleBufferGetPresentationTimeStamp(sample),
            duration: CMTime(value: 1, timescale: 30), frameProperties: properties,
            sourceFrameRefcon: nil, infoFlagsOut: nil)
        if result != noErr { onError?("Encode: \(result)") }
    }

    func stop() {
        if let session {
            VTCompressionSessionCompleteFrames(session, untilPresentationTimeStamp: .invalid)
            VTCompressionSessionInvalidate(session)
        }
        session = nil
    }

    private func set(_ key: CFString, _ value: CFTypeRef) throws {
        guard let session else { throw EncoderError.status(-1) }
        try check(VTSessionSetProperty(session, key: key, value: value))
    }

    private func check(_ status: OSStatus) throws {
        guard status == noErr else { throw EncoderError.status(status) }
    }

    private func output(_ sample: CMSampleBuffer) {
        guard CMSampleBufferDataIsReady(sample),
              let description = CMSampleBufferGetFormatDescription(sample),
              let block = CMSampleBufferGetDataBuffer(sample) else { return }
        do {
            var config = Data()
            var count = 0
            var headerLength: Int32 = 0
            try check(CMVideoFormatDescriptionGetH264ParameterSetAtIndex(description, parameterSetIndex: 0,
                parameterSetPointerOut: nil, parameterSetSizeOut: nil,
                parameterSetCountOut: &count, nalUnitHeaderLengthOut: &headerLength))
            for index in 0..<count {
                var pointer: UnsafePointer<UInt8>?
                var size = 0
                try check(CMVideoFormatDescriptionGetH264ParameterSetAtIndex(description, parameterSetIndex: index,
                    parameterSetPointerOut: &pointer, parameterSetSizeOut: &size,
                    parameterSetCountOut: nil, nalUnitHeaderLengthOut: nil))
                guard let pointer, size > 0 else { throw EncoderError.status(-1) }
                config.append(contentsOf: [0, 0, 0, 1])
                config.append(pointer, count: size)
            }
            var data = Data(count: CMBlockBufferGetDataLength(block))
            let copyStatus = data.withUnsafeMutableBytes { buffer in
                CMBlockBufferCopyDataBytes(block, atOffset: 0, dataLength: buffer.count, destination: buffer.baseAddress!)
            }
            try check(copyStatus)
            let attachments = CMSampleBufferGetSampleAttachmentsArray(sample, createIfNecessary: false) as? [[CFString: Any]]
            let key = (attachments?.first?[kCMSampleAttachmentKey_NotSync] as? Bool) != true
            let pts = CMTimeConvertScale(CMSampleBufferGetPresentationTimeStamp(sample), timescale: 1_000_000, method: .default)
            guard pts.isNumeric, pts.value >= 0 else { throw EncoderError.status(-1) }
            let bytes = try MCProtocol.annexB(data, lengthSize: Int(headerLength))
            onFrame?(EncodedFrame(configuration: config, bytes: bytes, pts: UInt64(pts.value), key: key))
        } catch { onError?("Неверный H.264 sample: \(error)") }
    }

    enum EncoderError: Error { case status(OSStatus) }
}
