import XCTest
@testable import MagerCamProtocol

final class MCProtocolTests: XCTestCase {
    func testWireHeaderMatchesExistingOBSReader() {
        let packet = MCProtocol.packet(.audioConfig, payload: Data([0, 0, 0xbb, 0x80, 1]))
        XCTAssertEqual(Array(packet), [0x4d, 0x43, 3, 0, 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xbb, 0x80, 1])
        let frame = MCProtocol.packet(.videoFrame, payload: Data([0x65]), pts: 1_000_000, key: true)
        XCTAssertEqual(Array(frame[8..<16]), [0, 0, 0, 0, 0, 0x0f, 0x42, 0x40])
        XCTAssertEqual(frame[3], 1)
    }

    func testMultipleNALsAndVariablePrefixLength() throws {
        XCTAssertEqual(try MCProtocol.annexB(Data([0, 2, 0x65, 0xaa, 0, 1, 0x06]), lengthSize: 2), Data([0, 0, 0, 1, 0x65, 0xaa, 0, 0, 0, 1, 0x06]))
        XCTAssertThrowsError(try MCProtocol.annexB(Data([0, 0, 0, 8, 0x65]), lengthSize: 4))
        XCTAssertThrowsError(try MCProtocol.annexB(Data([0, 0]), lengthSize: 4))
        XCTAssertThrowsError(try MCProtocol.annexB(Data([0]), lengthSize: 1))
    }

    func testRealOBSRequestAndFragmentedHeader() throws {
        let request = "GET /stream?w=1920&h=1080&fps=30&bitrate=8000&audio=1&camera=back&codec=hevc&bg=off HTTP/1.1\r\nHost: 192.168.1.23\r\nX-MagerCam-Key: 123456\r\n\r\n"
        XCTAssertNil(try HTTPRequest.parse(Data(request.dropLast().utf8)))
        let parsed = try XCTUnwrap(HTTPRequest.parse(Data(request.utf8)))
        XCTAssertEqual(parsed.path, "/stream")
        XCTAssertEqual(parsed.query["codec"], "hevc")
        XCTAssertEqual(parsed.accessKey, "123456")
    }

    func testQueryPriorityAndLastDuplicate() throws {
        let request = "GET /api/status?key=111111&key=222222&name=A+B HTTP/1.0\r\nX-MagerCam-Key: 333333\r\n\r\n"
        let parsed = try XCTUnwrap(HTTPRequest.parse(Data(request.utf8)))
        XCTAssertEqual(parsed.accessKey, "222222")
        XCTAssertEqual(parsed.query["name"], "A B")
        XCTAssertThrowsError(try HTTPRequest.parse(Data(repeating: 65, count: 16_385)))
    }
}
