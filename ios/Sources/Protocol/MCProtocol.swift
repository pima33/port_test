import Foundation

public enum MCProtocol {
    public enum Kind: UInt8 {
        case videoConfig = 1, videoFrame = 2, audioConfig = 3, audio = 4, status = 5, ping = 6
    }

    public static func packet(_ kind: Kind, payload: Data = Data(), pts: UInt64 = 0, key: Bool = false) -> Data {
        precondition(payload.count <= 32 * 1024 * 1024)
        var bytes = Data([0x4d, 0x43, kind.rawValue, key ? 1 : 0])
        append(UInt64(payload.count), count: 4, to: &bytes)
        append(pts, count: 8, to: &bytes)
        bytes.append(payload)
        return bytes
    }

    private static func append(_ value: UInt64, count: Int, to data: inout Data) {
        for byte in stride(from: count - 1, through: 0, by: -1) {
            data.append(UInt8(truncatingIfNeeded: value >> (byte * 8)))
        }
    }

    public enum ParseError: Error { case invalidNAL, invalidHTTP, headerTooLarge }

    /// VideoToolbox emits length-prefixed NAL units; MC consumers require Annex B.
    public static func annexB(_ input: Data, lengthSize: Int) throws -> Data {
        guard (1...4).contains(lengthSize), !input.isEmpty else { throw ParseError.invalidNAL }
        let bytes = [UInt8](input)
        var result = Data()
        var offset = 0
        while offset < bytes.count {
            guard bytes.count - offset >= lengthSize else { throw ParseError.invalidNAL }
            var length = 0
            for i in 0..<lengthSize { length = (length << 8) | Int(bytes[offset + i]) }
            offset += lengthSize
            guard length > 0, length <= bytes.count - offset else { throw ParseError.invalidNAL }
            result.append(contentsOf: [0, 0, 0, 1])
            result.append(contentsOf: bytes[offset..<(offset + length)])
            offset += length
        }
        return result
    }
}

public struct HTTPRequest {
    public let path: String
    public let query: [String: String]
    public let headers: [String: String]

    public static func parse(_ data: Data) throws -> HTTPRequest? {
        guard data.count <= 16_384 else { throw MCProtocol.ParseError.headerTooLarge }
        guard let end = data.range(of: Data("\r\n\r\n".utf8)) else { return nil }
        guard let text = String(data: data[..<end.lowerBound], encoding: .utf8) else {
            throw MCProtocol.ParseError.invalidHTTP
        }
        let lines = text.components(separatedBy: "\r\n")
        let first = lines[0].split(separator: " ")
        guard first.count == 3, first[0] == "GET", first[1].hasPrefix("/"),
              first[2] == "HTTP/1.0" || first[2] == "HTTP/1.1",
              let url = URLComponents(string: "http://localhost" + first[1]) else {
            throw MCProtocol.ParseError.invalidHTTP
        }
        var query: [String: String] = [:]
        // Form-style '+' decoding used by Android URLDecoder.
        if let raw = url.percentEncodedQuery {
            for item in raw.split(separator: "&") {
                let pair = item.split(separator: "=", maxSplits: 1, omittingEmptySubsequences: false)
                func decode(_ s: Substring) -> String? {
                    String(s).replacingOccurrences(of: "+", with: " ").removingPercentEncoding
                }
                guard let k = decode(pair[0]), let v = decode(pair.count == 2 ? pair[1] : "") else {
                    throw MCProtocol.ParseError.invalidHTTP
                }
                query[k] = v
            }
        }
        var headers: [String: String] = [:]
        for line in lines.dropFirst() {
            guard let colon = line.firstIndex(of: ":") else { throw MCProtocol.ParseError.invalidHTTP }
            headers[String(line[..<colon]).lowercased()] = String(line[line.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
        }
        return HTTPRequest(path: url.path, query: query, headers: headers)
    }

    public var accessKey: String? { query["key"] ?? headers["x-magercam-key"] }
}
