package service

import (
	"fmt"
	"github.com/gin-gonic/gin"
	"github.com/pion/rtp"
	"github.com/pion/rtp/codecs"
	"io"
	"log"
	"net"
)

const (
	maxDatagramSize = 8192
	// 1500 (UDP MTU) - 20 (IP header) - 8 (UDP header)
	maxPacketSize = 1472
)

type Service struct {
	r *gin.Engine
}

type Request struct {
	Proto string `uri:"proto" binding:"required"`
	Addr  string `uri:"addr" binding:"required"`
}

type Header struct {
	Host      string `header:"Host"`
	XRealIP   string `header:"X-Real-IP"`
	XRealPort string `header:"X-Real-PORT"`
}

type Response struct {
	Proto   string
	Addr    string
	Headers Header
}

func ReadUdpMulticastH264(UDP4MulticastAddress string, udpChan chan []byte) {

	log.Println("Waiting for a RTP/H264 stream on UDP port")

	mcaddr, err := net.ResolveUDPAddr("udp", UDP4MulticastAddress)
	if err != nil {
		fmt.Println("addr err:=", UDP4MulticastAddress)
	}

	socket, err := net.ListenMulticastUDP("udp", nil, mcaddr)

	data := make(chan []byte, 4096)

	go func() {
		for {
			udpData := make([]byte, 1452)
			n, _, err := socket.ReadFromUDP(udpData)
			if err != nil {
				fmt.Print("read udp stream err:=", err.Error())
			} else {
				if n > 12 {
					data <- udpData[:n]
				}
			}
		}
	}()

	go func() {
		var pkt rtp.Packet
		for {
			// parse RTP packet
			err := pkt.Unmarshal(<-data)
			if err != nil {
				fmt.Println("err:=", err.Error())
			} else {
				if len(pkt.Header.CSRC) > 0 {
					fmt.Println("CCRC size:", (len(pkt.Header.CSRC)))
				}

				// send to http stream
				fragment_type := int(pkt.Payload[0] & 0x1F)
				nal_type := int(pkt.Payload[1] & 0x1F)
				start_bit := int(pkt.Payload[1] & 0x80)
				end_bit := int(pkt.Payload[1] & 0x40)

				fmt.Println("head:", fragment_type, nal_type, start_bit, end_bit)

				h264pkt := codecs.H264Packet{
					IsAVC: true,
				}

				h264, err := h264pkt.Unmarshal(pkt.Payload)
				if err != nil {
					fmt.Println("decode h264 err:", err.Error())
				}

				if len(h264) > 0 {
					udpChan <- h264
				} else {
					fmt.Println("decode len 0")
				}

			}
		}
	}()

}

func NewService() *Service {
	r := gin.Default()

	r.GET("/:proto/:addr", func(c *gin.Context) {
		var req Request
		var headers Header
		// 解析请求参数
		if err := c.ShouldBindUri(&req); err != nil {
			c.JSON(400, gin.H{"msg": err.Error()})
			return
		}
		// 解析Headers
		if err := c.ShouldBindHeader(&headers); err != nil {
			fmt.Print(err.Error())
		}

		resp := &Response{
			Proto:   req.Proto,
			Addr:    req.Addr,
			Headers: headers,
		}

		if req.Proto != "udp" {
			c.JSON(200, resp)
			return
		}

		udpChan := make(chan []byte, 4096)
		go ReadUdpMulticastH264(req.Addr, udpChan)

		c.Stream(func(w io.Writer) bool {
			output, ok := <-udpChan
			if !ok {
				return false
			}
			_, err := c.Writer.Write(output)
			if err != nil {
				return false
			}
			return true
		})

	})

	r.GET("/", func(c *gin.Context) {
		c.JSON(200, gin.H{
			"message": "Hello World",
		})
	})

	srv := &Service{
		r: r,
	}
	return srv
}

func (s *Service) Run(addr ...string) error {
	// listen and serve on 0.0.0.0:8080 (for windows "localhost:8080")
	return s.r.Run(addr...)
}
