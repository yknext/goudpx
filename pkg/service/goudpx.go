package service

import (
	"encoding/hex"
	"fmt"
	"github.com/gin-gonic/gin"
	"io"
	"net"
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

func udpMulticastWriter(addr string, udpChan chan []byte) {

	defer close(udpChan)

	mcaddr, err := net.ResolveUDPAddr("udp", addr)
	if err != nil {
		fmt.Println("addr err:=", addr)
	}

	socket, err := net.ListenMulticastUDP("udp4", nil, mcaddr)
	for {
		data := make([]byte, 4096)
		n, _, err := socket.ReadFromUDP(data)
		fmt.Println("udp size:", n)
		if err != nil {
			fmt.Print("read udp stream err:=", err.Error())
		} else {
			hexf := hex.Dump(data)
			hexs := hex.Dump(data[:n])
			fmt.Println("hexf: % x" + hexf)
			fmt.Println("hexs: % x" + hexs)
			udpChan <- data[:n]
		}
	}
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

		udpChan := make(chan []byte)
		go udpMulticastWriter(req.Addr, udpChan)

		c.Stream(func(w io.Writer) bool {
			output, ok := <-udpChan
			if !ok {
				return false
			}
			_, err := c.Writer.Write(output)
			if err != nil {
				return false
			}
			c.Writer.Flush()
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
